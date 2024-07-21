using Codibre.MSSqlSession.Extensions;
using Codibre.MSSqlSession.Impl.Utils;

namespace Codibre.MSSqlSession.Impl;

internal class BatchQuery : IBatchQuery
{
    private static readonly FormattableString _beginTran = $"BEGIN TRAN;";
    private static readonly FormattableString _commitTran = $"COMMIT TRAN";
    private readonly IAsyncDbSession _session;
    private readonly IScriptBuilder _builder;
    private readonly TransactionControl _transactionControl = new();
    private readonly HookStorage _hookStorage;
    internal BatchQuery(IAsyncDbSession session)
    {
        _session = session;
        _builder = new ScriptBuilder(session.Connection);
        _hookStorage = new(
            new(),
            _builder
        );
    }

    public string Sql => _builder.Sql;
    public int QueryCount => _builder.QueryCount;
    public int ParamCount => _builder.ParamCount;

    public void AddNoResultScript(FormattableString builtScript) => _builder.Add(builtScript);

    public IResultHook<T> QueryFirstHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return _hookStorage.AddRequiredHook<T>(builtScript, token,
            async (reader, setResult) => setResult(token, await reader.ReadFirstAsync<T>())
        );
    }
    public IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return _hookStorage.AddNullableHook<T?>(builtScript, token,
            async (reader, setResult) => setResult(token, await reader.ReadFirstOrDefaultAsync<T>())
        );
    }

    public IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript)
    {
        var token = new object();
        return _hookStorage.AddRequiredHook<IEnumerable<T>>(builtScript, token,
            async (reader, setResult) => setResult(token, await reader.ReadAsync<T>())
        );
    }

    public Task RunQueries(TimeSpan? customTimeout = null)
        => _hookStorage.RunQueries(customTimeout);

    public Task Execute(TimeSpan? customTimeout = null)
        => _hookStorage.Execute(customTimeout);

    public void Clear() => _hookStorage.Clear();

    private IAsyncEnumerable<IList<KeyValuePair<TInput, TOutput>>> InternalPrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery
    ) => _hookStorage
            .PrepareEnumerable(enumerable, (current) => PreRunQuery(current, this))
            .OnStartAsync(async () => _session.ConnectionAcquired ? null : await _session.StartSession());

    public IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery
    ) => InternalPrepareEnumerable(enumerable, PreRunQuery)
            .SelectMany(x => x.ToAsyncEnumerable());

    private async ValueTask InternalFlushTransaction()
    {
        if (!_transactionControl.Open)
        {
            _transactionControl.Open = true;
            _ = await _session.StartTransaction();
        }
        await ExecuteInTransaction();
    }
    public async ValueTask AddTransactionScript(FormattableString builtScript)
    {
        _transactionControl.ValidateInTransaction();
        if (!_builder.TryAdd(builtScript))
        {
            await InternalFlushTransaction();
            _builder.Add(builtScript);
        }
    }

    public ValueTask FlushTransaction()
    {
        _transactionControl.ValidateInTransaction();
        return InternalFlushTransaction();
    }

    private async Task ExecuteInTransaction()
    {
        if (_transactionControl.WithHooks) await RunQueries();
        else await Execute(_transactionControl.Options.CustomTimeout);
    }

    private async Task CommitSingleRoundTripTransaction()
    {
        _builder.Prepend(_beginTran);
        AddNoResultScript(_commitTran);
        await ExecuteInTransaction();
    }

    private async Task CommitSplittedTransaction()
    {
        await ExecuteInTransaction();
        await _session.Commit();
    }

    private async Task RollBack()
    {
        await _session.Rollback();
        Clear();
    }

    public Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions? options = null)
        => RunInTransaction((_) => query(), options);

    public Task RunInTransaction(Action query, RunInTransactionOptions? options = null)
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

    public async Task RunInTransaction(Func<IBatchQuery, ValueTask> query, RunInTransactionOptions? options = null)
    {
        using var control = _transactionControl.Activate(options);
        if (_builder.QueryCount > 0) throw new InvalidOperationException("Query buffer not empty");
        try
        {
            await query(this);
            if (_transactionControl.Cancelled) await RollBack();
            else if (_transactionControl.Open) await CommitSplittedTransaction();
            else await CommitSingleRoundTripTransaction();
        }
        catch (Exception)
        {
            if (_transactionControl.Open) await _session.Rollback();
            throw;
        }
    }

    public void CancelTransaction()
    {
        _transactionControl.ValidateInTransaction();
        _transactionControl.Cancelled = true;
    }
}