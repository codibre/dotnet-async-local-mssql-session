using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Codibre.MSSqlSession.Impl;

// TODO: Remover quando https://github.com/dotnet/SqlClient/issues/979 for resolvido
// O bug acima faz com que OpenAsync enfileire chamadas de OpenAsync, o que causa
// um gargalo caso poucas conexões estejam disponíveis no pool e um volume alto de
// requisições chegue.
// Utilizar Task.Run força a obtenção da conexão ser feita em outra Thread, o que
// resolve paliativamente o problema, no entanto, trás o risco de esgotarmos a
// quantidade de threads disponíveis. Como usamos connection pool, esse risco é
// praticamente zero, dado que a obtenção da conexão geralmente é rápida e o volume
// de concorrência que lidamos também, mas o ideal é o bug ser corrigido na biblioteca
// pois lá eles tem condições de implementar alguma estratégia baseada em Tasks, não em
// Threads
internal class AsyncSqlConnection : DbConnection
{
    private string _connectionString;
    private string _dataSource;
    private bool _opened = false;
    private SqlConnection? _connection;
    private object? _pooledConnection;
    private readonly CrossedDisposer _disposer;
    private readonly bool _customPool;
    private readonly AsyncLocal<AsyncDbStorage?> _asyncDbStorage;
    private readonly ILogger _logger;
    public override string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (_connectionString != value)
            {
                _connection = null;
                ReleaseConnection();
            }
            _connectionString = value;
            _dataSource = GetDataSource(_connectionString);
        }
    }

    private static string GetDataSource(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return builder.DataSource;
    }

    public override string Database => _connection?.Database ?? throw new InvalidOperationException("Connection not opened yet");

    public override string DataSource => _dataSource;

    public override string ServerVersion => _connection?.ServerVersion ?? throw new InvalidOperationException("Connection no opened yet");

    public override ConnectionState State => _opened && _connection is not null ? _connection.State : ConnectionState.Closed;

    internal AsyncSqlConnection(
        string connectionString,
        CrossedDisposer disposer,
        AsyncLocal<AsyncDbStorage?> currentTransaction,
        bool customPool,
        ILogger logger
        )
    {
        _connectionString = connectionString;
        _disposer = disposer;
        _asyncDbStorage = currentTransaction;
        _customPool = customPool;
        _dataSource = GetDataSource(connectionString);
        _logger = logger;
    }

    private SqlConnection Connection
    {
        get
        {
            if (_connection is null)
            {
                if (_customPool) (_connection, _pooledConnection) = SqlConnectionFactory.GetConnection(ConnectionString, _logger);
                else _connection = new SqlConnection(ConnectionString);
            }
            return _connection;
        }
    }

    public override void ChangeDatabase(string databaseName)
    {
        if (_connection is null) throw new InvalidOperationException("Connection not opened yet");
        _connection.ChangeDatabase(databaseName);
    }

    public override void Close()
    {
        _disposer.Dispose();
        if (_pooledConnection is null && _connection is not null) _connection.Close();
        else ReleaseConnection();
        _connection = null;
        _opened = false;
    }

    private async void ReleaseConnection()
    {
        try
        {
            var transaction = _asyncDbStorage.Value?.Transaction;
            if (transaction is not null) await AsyncDbSession.InternalRollback(transaction);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Error during connection release");
        }
        finally
        {
            SqlConnectionFactory.ReleaseConnection(_pooledConnection);
        }
    }

    public override void Open()
    {
        var conn = Connection;
        if (conn.State == ConnectionState.Closed) conn.Open();
        _opened = true;
    }

    public override Task OpenAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var conn = Connection;
        if (conn.State == ConnectionState.Closed) await conn.OpenAsync(cancellationToken);
        _opened = true;
    });

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (State != ConnectionState.Closed) return Connection.BeginTransaction();
        Open();
        return new AsyncDbTransaction(
            Connection.BeginTransaction(),
            this
        );
    }

    protected override DbCommand CreateDbCommand()
    {
        var command = new AsyncDbCommand(Connection.CreateCommand(), this);
        var value = _asyncDbStorage.Value;
        if (value != null)
        {
            command.Transaction = _asyncDbStorage.Value?.Transaction;
        }
        return command;
    }

#if NETSTANDARD2_1_OR_GREATER
    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        if (State != ConnectionState.Closed) return await base.BeginDbTransactionAsync(isolationLevel, cancellationToken);
        await OpenAsync();
        var transaction = await Connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new AsyncDbTransaction(
            (SqlTransaction)transaction,
            this
        );
    }

    public override async Task CloseAsync()
    {
        _disposer.Dispose();
        if (_pooledConnection is null && _connection is not null) await _connection.CloseAsync();
        else ReleaseConnection();
        _connection = null;
        _opened = false;
    }
#endif

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseConnection();
            _connection = null;
        }
        base.Dispose(disposing);
    }
}

public class AsyncDbTransaction : DbTransaction
{
    internal SqlTransaction InternalDbTransaction { get; }
    public override IsolationLevel IsolationLevel => InternalDbTransaction.IsolationLevel;

    protected override DbConnection DbConnection => InternalDbTransaction.Connection;
    private readonly DbConnection _connection;

    internal AsyncDbTransaction(
        SqlTransaction dbTransaction,
        DbConnection connection
    )
    {
        InternalDbTransaction = dbTransaction;
        _connection = connection;
    }

    public override void Commit()
    {
        InternalDbTransaction.Commit();
        _connection.Close();
    }

    public override void Rollback()
    {
        InternalDbTransaction.Rollback();
        _connection.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) InternalDbTransaction.Dispose();
        base.Dispose(disposing);
    }

#if NETSTANDARD2_1_OR_GREATER
    public override async ValueTask DisposeAsync()
    {
        await InternalDbTransaction.DisposeAsync();
        await base.DisposeAsync();
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await InternalDbTransaction.RollbackAsync(cancellationToken);
        await _connection.CloseAsync();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await InternalDbTransaction.CommitAsync();
        await _connection.CloseAsync();
    }
#endif

    public override object InitializeLifetimeService() => InternalDbTransaction.InitializeLifetimeService();
    public override bool Equals(object obj) => InternalDbTransaction.Equals(obj);
    public override int GetHashCode() => InternalDbTransaction.GetHashCode();

    public override string ToString() => InternalDbTransaction.ToString();

    public static implicit operator SqlTransaction(AsyncDbTransaction transaction) => transaction.InternalDbTransaction;
}

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