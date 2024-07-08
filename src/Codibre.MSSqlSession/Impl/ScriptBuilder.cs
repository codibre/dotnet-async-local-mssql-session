using System.Data.Common;
using DapperQueryBuilder;
using InterpolatedSql.Dapper.SqlBuilders;
using static Dapper.SqlMapper;

namespace Codibre.MSSqlSession.Impl;

internal class ScriptBuilder : IScriptBuilder
{
    public int QueryCount { get; private set; }
    public int ParamLimit { get; } = 2100;
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

    public int ParamCount => _queryBuilder.Parameters.Count;

    public void Add(FormattableString query)
    {
        if (ParamCount + query.ArgumentCount > ParamLimit)
            throw new InvalidOperationException("Parameter limit reached");
        QueryCount++;
        _queryBuilder += query;
        EnsureSemiColon();
    }

    private void EnsureSemiColon()
    {
        if (!_queryBuilder.Format.EndsWith(";")) _queryBuilder += $";";
    }

    public void Clear()
    {
        QueryCount = 0;
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
        var current = _queryBuilder;
        _queryBuilder = new QueryBuilder(_connection, query);
        EnsureSemiColon();
        Add(current);
    }
}