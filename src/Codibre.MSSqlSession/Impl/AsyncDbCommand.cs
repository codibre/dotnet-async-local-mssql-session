using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Codibre.MSSqlSession.Impl;

internal class AsyncDbCommand : DbCommand
{
    private readonly DbCommand _command;
    internal AsyncDbCommand(DbCommand command, AsyncSqlConnection connection)
    {
        _command = command;
        DbConnection = connection;
    }

    public override string CommandText
    {
        get => _command.CommandText;
        set => _command.CommandText = value;
    }
    public override int CommandTimeout
    {
        get => _command.CommandTimeout;
        set => _command.CommandTimeout = value;
    }
    public override CommandType CommandType
    {
        get => _command.CommandType;
        set => _command.CommandType = value;
    }
    public override bool DesignTimeVisible
    {
        get => _command.DesignTimeVisible;
        set => _command.DesignTimeVisible = value;
    }
    public override UpdateRowSource UpdatedRowSource
    {
        get => _command.UpdatedRowSource;
        set => _command.UpdatedRowSource = value;
    }
    protected override DbConnection DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => _command.Parameters;

    protected override DbTransaction DbTransaction
    {
        get => _command.Transaction;
        set
        {
            var transaction = value;
            if (value is AsyncDbTransaction asyncValue) transaction = asyncValue.InternalDbTransaction;
            if (value is SqlTransaction sqlTransaction) transaction = sqlTransaction;
            _command.Transaction = transaction;
        }
    }

    public override void Cancel() => _command.Cancel();

    public override int ExecuteNonQuery() => _command.ExecuteNonQuery();

    public override object ExecuteScalar() => _command.ExecuteScalar();

    public override void Prepare() => _command.Prepare();

    protected override DbParameter CreateDbParameter() => _command.CreateParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _command.ExecuteReader(behavior);
}