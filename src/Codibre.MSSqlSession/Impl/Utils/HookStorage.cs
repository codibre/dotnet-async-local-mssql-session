using static Dapper.SqlMapper;

namespace Codibre.MSSqlSession.Impl.Utils;

internal class HookStorage
{
    internal delegate bool SetResultDelegate(object token, object? value);
    private readonly TransactionControl _transactionControl;
    private static readonly object _waiting = new();
    private readonly Dictionary<object, object?> _results = new();
    private readonly IScriptBuilder _builder;
    private readonly List<Func<GridReader, SetResultDelegate, Task>> _hooks = new();
    internal HookStorage(
        TransactionControl transactionControl,
        IScriptBuilder builder
    )
    {
        _transactionControl = transactionControl;
        _builder = builder;
    }

    private bool SetResult(object token, object? result)
    {
        if (_results.TryGetValue(token, out var previous) && previous != _waiting) return false;
        _results[token] = result;
        return true;
    }

    internal IResultHook<T> AddRequiredHook<T>(
        FormattableString builtScript,
        object token,
        Func<GridReader, SetResultDelegate, Task> hook
    )
    {
        if (_transactionControl.Active) _transactionControl.WithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
        }

        return new ResultHookCallback<T>(() => GetRequired<T>(token));
    }

    internal IResultHook<T?> AddNullableHook<T>(FormattableString builtScript, object token, Func<GridReader, SetResultDelegate, Task> hook)
    {
        if (_transactionControl.Active) _transactionControl.WithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
        }

        return new ResultHookCallback<T?>(() => GetOrDefault<T>(token));
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

    internal void Clear()
    {
        _results.Clear();
        ClearPendingRun();
    }

    internal void ClearPendingRun()
    {
        _transactionControl.WithHooks = false;
        _builder.Clear();
        _hooks.Clear();
    }

    internal async Task RunQueries(TimeSpan? customTimeout = null)
    {
        if (_builder.QueryCount <= 0) return;
        var reader = await _builder.QueryMultipleAsync(customTimeout);
        var hookEnumerator = _hooks.GetEnumerator();
        while (!reader.IsConsumed)
        {
            if (!hookEnumerator.MoveNext())
                throw new InvalidDataException("Number of results is greater than the number of hooks!");
            var hook = hookEnumerator.Current;
            await hook(reader, SetResult);
        }
        if (hookEnumerator.MoveNext()) throw new InvalidDataException("Number of results is lesser than the number of hooks!");
        ClearPendingRun();
    }

    internal async Task Execute(TimeSpan? customTimeout = null)
    {
        if (_transactionControl.WithHooks) throw new InvalidOperationException("Operation invalid for hooked transaction");
        if (_builder.QueryCount <= 0) return;
        await _builder.ExecuteAsync(customTimeout);
        ClearPendingRun();
    }

    internal async IAsyncEnumerable<IList<KeyValuePair<TInput, TOutput>>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ValueTask<TOutput>> PreRunQuery
    )
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
                    var preparedValue = await PreRunQuery(current);
                    result.Add(new KeyValuePair<TInput, TOutput>(current, preparedValue));
                    hasNext = enumerator.MoveNext();
                } while (hasNext && _builder.ParamCount < _builder.ParamLimit);
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