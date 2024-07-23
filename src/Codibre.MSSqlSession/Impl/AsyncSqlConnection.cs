using System.Data;
using System.Data.Common;
using Codibre.MSSqlSession.Impl.Utils;
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
    private readonly AsyncSqlOptions _options;
    private string _dataSource;
    private bool _opened = false;
    private SqlConnection? _connection;
    private object? _pooledConnection;
    private readonly CrossedDisposer _disposer;
    private readonly AsyncLocal<AsyncDbStorage?> _asyncDbStorage;
    private readonly ILogger _logger;
    public override string ConnectionString
    {
        get => _options.ConnectionString;
        set
        {
            if (_options.ConnectionString != value)
            {
                _connection = null;
                ReleaseConnection();
            }
            _options.ConnectionString = value;
            _dataSource = GetDataSource(_options.ConnectionString);
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
        AsyncSqlOptions options,
        CrossedDisposer disposer,
        AsyncLocal<AsyncDbStorage?> currentTransaction,
        ILogger logger
        )
    {
        _options = options;
        _disposer = disposer;
        _asyncDbStorage = currentTransaction;
        _dataSource = GetDataSource(_options.ConnectionString);
        _logger = logger;
    }

    private SqlConnection Connection
    {
        get
        {
            if (_connection is null)
            {
                if (_options.CustomPool) (_connection, _pooledConnection) = SqlConnectionFactory.GetConnection(ConnectionString, _logger);
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

    private void ReleaseConnection()
    => _ = Task.Run(async () =>
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
    });

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
        var sqlCommand = Connection.CreateCommand();
        if (_options.RetryLogicBaseProvider is not null) sqlCommand.RetryLogicProvider = _options.RetryLogicBaseProvider;
        var command = new AsyncDbCommand(sqlCommand, this);
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