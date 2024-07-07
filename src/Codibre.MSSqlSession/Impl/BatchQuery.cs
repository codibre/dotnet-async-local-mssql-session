using static Dapper.SqlMapper;

namespace Codibre.MSSqlSession.Impl;

internal class ResultHookCallback<T> : IResultHook<T>
{
    private readonly Func<T> _result;
    public T Result => _result();

    internal ResultHookCallback(Func<T> result) => _result = result;
}

internal class BatchQuery : IBatchQuery
{
    private static readonly FormattableString _beginTran = $"BEGIN TRAN;";
    private static readonly FormattableString _commitTran = $"COMMIT TRAN";
    private static readonly object _waiting = new();
    private readonly IAsyncDbSession _session;
    private readonly IScriptBuilder _builder;
    private readonly Dictionary<object, object?> _results = new();
    private readonly List<Func<GridReader, Task>> _hooks = new();
    private bool _inTransaction = false;
    private bool _transactionOpen = false;
    private bool _transactionCanceled = false;
    private bool _transactionWithHooks = false;
    private RunInTransactionOptions? _transactionOptions = null;
    internal BatchQuery(IAsyncDbSession session)
    {
        _session = session;
        _builder = new ScriptBuilder(session.Connection);
    }

    public string Sql => _builder.Sql;
    public int QueryCount => _builder.QueryCount;
    public int ParamCount => _builder.ParamCount;

    public void AddNoResultScript(FormattableString builtScript) => _builder.Add(builtScript);

    private bool SetResult(object token, object? result)
    {
        if (_results.TryGetValue(token, out var previous) && previous != _waiting) return false;
        _results[token] = result;
        return true;
    }

    private IResultHook<T> AddRequiredHook<T>(FormattableString builtScript, object token, Func<GridReader, Task> hook)
    {
        if (_inTransaction) _transactionWithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
        }

        return new ResultHookCallback<T>(() => GetRequired<T>(token));
    }

    private IResultHook<T?> AddNullableHook<T>(FormattableString builtScript, object token, Func<GridReader, Task> hook)
    {
        if (_inTransaction) _transactionWithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
        }

        return new ResultHookCallback<T?>(() => GetOrDefault<T>(token));
    }

    public IResultHook<T> QueryFirstHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return AddRequiredHook<T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstAsync<T>())
        );
    }
    public IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return AddNullableHook<T?>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstOrDefaultAsync<T>())
        );
    }

    public IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return AddRequiredHook<IEnumerable<T>>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadAsync<T>())
        );
    }

    public async Task RunQueries(TimeSpan? customTimeout = null)
    {
        if (_builder.QueryCount <= 0) return;
        var reader = await _builder.QueryMultipleAsync(customTimeout);
        var hookEnumerator = _hooks.GetEnumerator();
        while (!reader.IsConsumed)
        {
            if (!hookEnumerator.MoveNext())
                throw new InvalidDataException("Number of results is greater than the number of hooks!");
            var hook = hookEnumerator.Current;
            await hook(reader);
        }
        if (hookEnumerator.MoveNext()) throw new InvalidDataException("Number of results is lesser than the number of hooks!");
        ClearPendingRun();
    }

    public async Task Execute(TimeSpan? customTimeout = null)
    {
        if (_transactionWithHooks) throw new InvalidOperationException("Operation invalid for hooked transaction");
        if (_builder.QueryCount <= 0) return;
        await _builder.ExecuteAsync(customTimeout);
        ClearPendingRun();
    }

    private T? GetOrDefault<T>(object token)
    {
        var result = _results[token];
        if (result == _waiting) throw new InvalidOperationException("Query not executed yet!");
        if (result is T resultT) return resultT;
        if (result is null) return default;
        throw new InvalidOperationException($"Type {nameof(T)} incompatible with provided token");
    }

    private T GetRequired<T>(object token)
    {
        var result = _results[token];
        if (result == _waiting) throw new InvalidOperationException("Query not executed yet!");
        if (result is T resultT) return resultT;
        if (result is null) throw new InvalidDataException("Result Non Nullable");
        throw new InvalidOperationException($"Type {nameof(T)} incompatible with provided token");
    }

    public void Clear()
    {
        _results.Clear();
        ClearPendingRun();
    }

    private void ClearPendingRun()
    {
        _transactionWithHooks = false;
        _builder.Clear();
        _hooks.Clear();
    }

    private async IAsyncEnumerable<IList<KeyValuePair<TInput, TOutput>>> InternalPrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery,
        int paramMargin = 100
    )
    {
        using (_session.ConnectionAcquired ? null : await _session.StartSession())
        {
            try
            {
                var enumerator = enumerable.GetEnumerator();
                var hasNext = enumerator.MoveNext();
                while (hasNext)
                {
                    var result = new List<KeyValuePair<TInput, TOutput>>();
                    do
                    {
                        var current = enumerator.Current;
                        var preparedValue = await PreRunQuery(current, this);
                        result.Add(new KeyValuePair<TInput, TOutput>(current, preparedValue));
                        hasNext = enumerator.MoveNext();
                    } while (hasNext && ParamCount + paramMargin < _builder.ParamLimit);
                    await RunQueries();
                    yield return result;
                }
            }
            finally
            {
                ClearPendingRun();
            }
        }
    }

    public IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery,
        int paramMargin = 100
    ) => InternalPrepareEnumerable(enumerable, PreRunQuery, paramMargin)
            .SelectMany(x => x.ToAsyncEnumerable());

    private async ValueTask InternalFlushTransaction()
    {
        if (!_transactionOpen)
        {
            _transactionOpen = true;
            _ = await _session.StartTransaction();
        }
        await ExecuteInTransaction();
    }
    public async ValueTask AddTransactionScript(FormattableString builtScript)
    {
        ValidateInTransaction();
        if (_builder.ParamCount + _transactionOptions!.ParamMargin >= _builder.ParamLimit)
        {
            await InternalFlushTransaction();
        }
        _builder.Add(builtScript);
    }

    public ValueTask FlushTransaction()
    {
        ValidateInTransaction();
        return InternalFlushTransaction();
    }

    private void ValidateInTransaction()
    {
        if (!_inTransaction) throw new InvalidOperationException("Must run inside RunInTransaction callback");
    }

    private async Task ExecuteInTransaction()
    {
        if (_transactionWithHooks) await RunQueries();
        else await Execute(_transactionOptions!.CustomTimeout);
    }

    public Task RunInTransaction(Func<IBatchQuery, ValueTask> query, int paramMargin = 100)
        => RunInTransaction(query, new RunInTransactionOptions
        {
            ParamMargin = paramMargin
        });

    public Task RunInTransaction(Func<ValueTask> query, int paramMargin = 100)
        => RunInTransaction((_) => query(), paramMargin);

    public Task RunInTransaction(Action<IBatchQuery> query, int paramMargin = 100)
        => RunInTransaction((bq) =>
        {
            query(bq);
            return new ValueTask();
        }, paramMargin);

    public Task RunInTransaction(Action query, int paramMargin = 100)
        => RunInTransaction((bq) =>
        {
            query();
            return new ValueTask();
        }, paramMargin);

    public async Task RunInTransaction(Func<IBatchQuery, ValueTask> query, RunInTransactionOptions options)
    {
        if (_inTransaction) throw new InvalidOperationException("RunInTransaction Already called");
        if (_builder.QueryCount > 0) throw new InvalidOperationException("Query buffer not empty");
        try
        {
            _inTransaction = true;
            _transactionOpen = false;
            _transactionCanceled = false;
            _transactionWithHooks = false;
            _transactionOptions = options;
            await query(this);
            if (_transactionCanceled)
            {
                await _session.Rollback();
                Clear();
            }
            else if (_transactionOpen)
            {
                await ExecuteInTransaction();
                await _session.Commit();
            }
            else
            {
                _builder.Prepend(_beginTran);
                AddNoResultScript(_commitTran);
                await ExecuteInTransaction();
            }
        }
        catch (Exception)
        {
            if (_transactionOpen) await _session.Rollback();
            throw;
        }
        finally
        {
            _inTransaction = false;
            _transactionOpen = false;
            _transactionCanceled = false;
            _transactionWithHooks = false;
            _transactionOptions = null;
        }
    }

    public Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions options)
        => RunInTransaction((_) => query(), options);

    public Task RunInTransaction(Action query, RunInTransactionOptions options)
        => RunInTransaction((_) =>
        {
            query();
            return new ValueTask();
        }, options);

    public async Task<T> RunInTransaction<T>(Func<ValueTask<T>> query, RunInTransactionOptions? options = null)
        => await RunInTransaction((_) => query(), options);

    public async Task<T> RunInTransaction<T>(Func<IBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null)
    {
        T? result = default;
        if (options is null) await RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        });
        else await RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        }, options);
        return result!;
    }

    public void CancelTransaction()
    {
        ValidateInTransaction();
        _transactionCanceled = true;
    }
}

public static class BatchQueryExtension
{
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        IBatchQuery batchQuery,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery,
        int paramMargin = 100
    ) => batchQuery.PrepareEnumerable(enumerable, PreRunQuery, paramMargin);
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        IBatchQuery batchQuery,
        Func<TInput, IBatchQuery, TOutput> PreRunQuery,
        int paramMargin = 100
    ) => batchQuery.PrepareEnumerable(
        enumerable,
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq)),
        paramMargin
    );
}