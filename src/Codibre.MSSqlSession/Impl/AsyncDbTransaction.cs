using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Codibre.MSSqlSession.Impl;

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