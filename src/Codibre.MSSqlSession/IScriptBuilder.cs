using static Dapper.SqlMapper;

namespace Codibre.MSSqlSession;

public interface IScriptBuilder
{
    void Prepend(FormattableString query);
    void Add(FormattableString query);
    bool TryAdd(FormattableString query);
    void Clear();
    Task ExecuteAsync(TimeSpan? customTimeout = null);
    Task<IEnumerable<T>> QueryAsync<T>();
    Task<T> QueryFirstAsync<T>();
    Task<T> QueryFirstOrDefaultAsync<T>();
    Task<GridReader> QueryMultipleAsync(TimeSpan? customTimeout = null);
    string Sql { get; }
    FormattableString FormattableQuery { get; }
    int QueryCount { get; }
    int ParamCount { get; }
    int ParamLimit { get; }
}