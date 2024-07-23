using Codibre.MSSqlSession.Impl.Utils;

namespace Codibre.MSSqlSession.Impl;

internal partial class BatchQuery
{
    private static readonly FormattableString _beginTran = $"BEGIN TRAN;";
    private static readonly FormattableString _commitTran = $"COMMIT TRAN";
    private readonly TransactionControl _transactionControl = new();
    private async ValueTask InternalFlushTransaction()
    {
        if (!_transactionControl.Open)
        {
            _transactionControl.Open = true;
            _ = await _session.StartTransaction();
        }
        await ExecuteInTransaction();
    }
    public async ValueTask AddTransactionScript(FormattableString builtScript)
    {
        _transactionControl.ValidateInTransaction();
        if (!_builder.TryAdd(builtScript))
        {
            await InternalFlushTransaction();
            _builder.Add(builtScript);
        }
    }

    public ValueTask FlushTransaction()
    {
        _transactionControl.ValidateInTransaction();
        return InternalFlushTransaction();
    }

    private async Task ExecuteInTransaction()
    {
        if (_transactionControl.WithHooks) await RunQueries();
        else await Execute(_transactionControl.Options.CustomTimeout);
    }

    private async Task CommitSingleRoundTripTransaction()
    {
        _builder.Prepend(_beginTran);
        AddNoResultScript(_commitTran);
        await ExecuteInTransaction();
    }

    private async Task CommitSplittedTransaction()
    {
        await ExecuteInTransaction();
        await _session.Commit();
    }

    private async Task RollBack()
    {
        await _session.Rollback();
        Clear();
    }

    public Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions? options = null)
        => RunInTransaction((_) => query(), options);

    public Task RunInTransaction(Action query, RunInTransactionOptions? options = null)
        => RunInTransaction((_) =>
        {
            query();
            return new ValueTask();
        }, options);

    public Task<T> RunInTransaction<T>(Func<ValueTask<T>> query, RunInTransactionOptions? options = null)
        => RunInTransaction((_) => query(), options);

    public async Task<T> RunInTransaction<T>(Func<IBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null)
    {
        T? result = default;
        if (options is null) await RunInTransaction(async (bq) =>
            {
                result = await query(bq);
            });
        else await RunInTransaction(async (bq) =>
            {
                result = await query(bq);
            }, options);
        return result!;
    }

    public async Task RunInTransaction(Func<IBatchQuery, ValueTask> query, RunInTransactionOptions? options = null)
    {
        using var control = _transactionControl.Activate(options);
        if (_builder.QueryCount > 0) throw new InvalidOperationException("Query buffer not empty");
        try
        {
            await query(this);
            if (_transactionControl.Cancelled) await RollBack();
            else if (_transactionControl.Open) await CommitSplittedTransaction();
            else await CommitSingleRoundTripTransaction();
        }
        catch (Exception)
        {
            if (_transactionControl.Open) await RollBack();
            throw;
        }
    }

    public void CancelTransaction()
    {
        _transactionControl.ValidateInTransaction();
        _transactionControl.Cancelled = true;
    }
}