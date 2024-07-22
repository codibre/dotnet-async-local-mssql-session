using System.Data.Common;
using Codibre.MSSqlSession.Impl.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Codibre.MSSqlSession.Impl;

internal sealed class DummyDisposable : IDisposable
{
    public void Dispose()
    {
        // Dummy
    }
}

internal sealed class AsyncDbSession : IAsyncDbSession
{
    private static readonly Task<IDisposable?> _nullTask = Task.FromResult((IDisposable?)null);
    private static readonly object _updateToken = new();
    private static DateTime _omitLogDeadline = DateTime.Now;
    private readonly bool _customPool;
    private readonly AsyncLocal<AsyncDbStorage?> _asyncStorage = new();
    private readonly ILogger<IAsyncDbSession> _logger;
    private readonly string _connectionString;
    public DbTransaction? Transaction
    {
        get => _asyncStorage.Value?.Transaction;
        set => Storage.Transaction = value;
    }
    public DbConnection Connection => Storage.Connection;

    private AsyncDbStorage Storage
    {
        get
        {
            if (_asyncStorage.Value is null) _ = CreateAsyncLocalConnection();
            return _asyncStorage.Value!;
        }
    }

    public IBatchQuery BatchQuery
    {
        get
        {
            var storage = Storage;
            return storage.BatchQuery ??= new BatchQuery(this);
        }
    }

    public bool ConnectionAcquired => _asyncStorage.Value != null;

    public AsyncDbSession(
        IConfiguration configuration,
        ILogger<AsyncDbSession> logger
    )
    : this(
        configuration.GetConnectionString("SqlConnection") ?? throw new ArgumentException("Null connection string"),
        configuration.GetSection("SqlConfig").GetSection("CustomPool").Value?.ToUpper() == "TRUE",
        logger
    )
    { }

    public AsyncDbSession(
        string connectionString,
        bool customPool,
        ILogger<AsyncDbSession> logger
    )
    {
        _logger = logger;
        _customPool = customPool;
        _connectionString = connectionString;
    }

    public async ValueTask Clear()
    {
        if (Transaction is null) await Commit();
        await CloseConn(Connection);
    }

    public async Task Commit()
    {
        var transaction = Transaction;
        if (transaction is null) return;
        Transaction = null;
        await InternalCommit(transaction);
    }

    public void Dispose()
    {
        var storage = _asyncStorage.Value;
        if (storage is null) return;
        if (storage.Transaction is not null) Helper.Try(() => storage.Transaction.Rollback());
        if (storage.Connection is not null)
        {
            Helper.Try(() => storage.Connection.Close());
            Helper.Try(() => storage.Connection.Dispose());
        }
        _asyncStorage.Value = null;
    }

    public Task Run(Func<Task> actions)
        => Run(async () =>
        {
            await actions();
            return 0;
        });

    public async Task<T> Run<T>(Func<Task<T>> actions)
    {
        using (await StartSession())
        {
            return await actions();
        }
    }

    public Task<IDisposable?> StartSession()
    {
        if (ConnectionAcquired)
        {
            var now = DateTime.Now;
            if (_omitLogDeadline < now)
            {
                var trace = new System.Diagnostics.StackTrace();
                _logger.LogWarning("Nested IAsyncDBSession.StartSession call! Review the code! {Trace}", trace);
                UpdateOmitLogDeadline(now);
            }
            return _nullTask;
        }
        var (connection, disposer) = CreateAsyncLocalConnection();
        return ConnectAndReturnDisposable(connection, disposer);
    }

    private static void UpdateOmitLogDeadline(DateTime now)
    {
        lock (_updateToken)
        {
            _omitLogDeadline = now.AddHours(1);
        }
    }

    private (AsyncSqlConnection, CrossedDisposer) CreateAsyncLocalConnection()
    {
        var disposer = new CrossedDisposer(this);
        var connection = new AsyncSqlConnection(
            _connectionString,
            disposer,
            _asyncStorage,
            _customPool,
            _logger
        );
        _asyncStorage.Value = new AsyncDbStorage(connection);
        return (connection, disposer);
    }

    private async Task<IDisposable?> ConnectAndReturnDisposable(AsyncSqlConnection connection, CrossedDisposer disposer)
    {
        await connection.OpenAsync();
        return disposer;
    }

    public async Task Rollback()
    {
        var transaction = Transaction;
        if (transaction is null) return;
        try
        {
            await InternalRollback(transaction);
            Transaction = null;
        }
        finally
        {
            Transaction = null;
        }
    }

    public Task<IDisposable> StartTransaction()
    {
        if (ConnectionAcquired) return InternalStartTransaction();
        var task = StartSession();
        return AsyncInternalStartTransaction(task);
    }

    private async Task<IDisposable> AsyncInternalStartTransaction(Task<IDisposable?> task)
    {
        var result = await task;
        var transactionDisposer = await InternalStartTransaction();
        return new CallbackDisposer(() =>
        {
            transactionDisposer.Dispose();
            result?.Dispose();
        });
    }

    private async Task<IDisposable> InternalStartTransaction()
    {
        Transaction = await BeginTransaction();
        return new CallbackDisposer(() => Transaction?.Rollback());
    }

    public IScriptBuilder CreateScriptBuilder()
        => Transaction is null ? new ScriptBuilder(Connection) : new ScriptBuilder(Transaction);

#if NETSTANDARD2_1_OR_GREATER
    internal static Task CloseConn(DbConnection connection) => connection.CloseAsync();
    private ValueTask<DbTransaction> BeginTransaction() => Connection.BeginTransactionAsync();
    internal static Task InternalCommit(DbTransaction transaction) => transaction.CommitAsync();
    internal static Task InternalRollback(DbTransaction transaction) => transaction.RollbackAsync();
#else
    internal static Task CloseConn(DbConnection connection) => Task.Run(() => connection.Close());
    private ValueTask<DbTransaction> BeginTransaction() => new(Task.Run(() => Connection.BeginTransaction()));
    internal static Task InternalCommit(DbTransaction transaction) => Task.Run(() => transaction.Commit());
    internal static Task InternalRollback(DbTransaction transaction) => Task.Run(() => transaction.Rollback());
#endif
}