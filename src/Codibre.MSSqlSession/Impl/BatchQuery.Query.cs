using Codibre.MSSqlSession.Extensions;
using Codibre.MSSqlSession.Impl.Utils;

namespace Codibre.MSSqlSession.Impl;

internal partial class BatchQuery : IBatchQuery
{
    private readonly IAsyncDbSession _session;
    private readonly IScriptBuilder _builder;
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
}