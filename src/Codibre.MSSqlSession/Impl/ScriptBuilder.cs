using System.Collections;
using System.Data.Common;
using DapperQueryBuilder;
using InterpolatedSql.Dapper.SqlBuilders;
using static Dapper.SqlMapper;

namespace Codibre.MSSqlSession.Impl;

internal class ScriptBuilder : IScriptBuilder
{
    public int QueryCount { get; private set; }
    public int ParamLimit { get; } = 2000;
    private readonly DbTransaction? _transaction;
    private readonly DbConnection _connection;
    private QueryBuilder _queryBuilder;
    public FormattableString FormattableQuery => _queryBuilder;

    internal ScriptBuilder(
        DbConnection connection
    )
    {
        _transaction = null;
        _connection = connection;
        _queryBuilder = connection.QueryBuilder();
    }
    internal ScriptBuilder(
        DbTransaction transaction
    )
    {
        _transaction = transaction;
        _connection = _transaction.Connection;
        _queryBuilder = _connection.QueryBuilder();
    }

    public string Sql => _queryBuilder.Sql;

    public int ParamCount { get; private set; }

    private int GetRealCount(FormattableString query)
        => GetRealCount(query.GetArguments());
    private int GetRealCount(IEnumerable<object> arguments)
        => arguments.Select(x =>
        {
            if (x is ICollection collection) return collection.Count;
            return 1;
        }).Sum();

    public void Add(FormattableString query)
        => AddInternal(query, GetRealCount(query));

    private void AddInternal(FormattableString query, int realCount)
    {
        if (!TryAddInternal(query, realCount))
            throw new InvalidOperationException("Parameter limit reached");
    }

    public bool TryAdd(FormattableString query)
        => TryAddInternal(query, GetRealCount(query));

    private bool TryAddInternal(FormattableString query, int realCount)
    {
        if (ParamCount + realCount > ParamLimit) return false;
        QueryCount++;
        ParamCount += realCount;
        _queryBuilder += query;
        EnsureSemiColon();
        return true;
    }

    private void EnsureSemiColon()
    {
        if (!_queryBuilder.Format.EndsWith(";")) _queryBuilder += $";";
    }

    public void Clear()
    {
        QueryCount = 0;
        ParamCount = 0;
        _queryBuilder = _connection.QueryBuilder();
    }

    public Task ExecuteAsync(TimeSpan? customTimeout = null)
        => _queryBuilder.ExecuteAsync(_transaction, commandTimeout: (int?)customTimeout?.TotalSeconds);
    public Task<IEnumerable<T>> QueryAsync<T>() => _queryBuilder.QueryAsync<T>(_transaction);

    public Task<T> QueryFirstOrDefaultAsync<T>() => _queryBuilder.QueryFirstOrDefaultAsync<T>(_transaction);
    public Task<T> QueryFirstAsync<T>() => _queryBuilder.QueryFirstAsync<T>(_transaction);
    public Task<GridReader> QueryMultipleAsync(TimeSpan? customTimeout = null)
        => _queryBuilder.QueryMultipleAsync(
            commandTimeout: (int?)customTimeout?.TotalSeconds
        );

    public void Prepend(FormattableString query)
    {
        QueryCount = 1;
        var realCount = ParamCount;
        ParamCount = GetRealCount(query);
        var current = _queryBuilder;
        _queryBuilder = new QueryBuilder(_connection, query);
        EnsureSemiColon();
        AddInternal(current, realCount);
    }
}