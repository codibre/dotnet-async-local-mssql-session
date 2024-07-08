namespace Codibre.MSSqlSession;

public interface IResultHook<out T>
{
    T Result { get; }
}

public class RunInTransactionOptions
{
    public int ParamMargin { get; set; } = 100;
    public TimeSpan? CustomTimeout { get; set; }
}

public interface IBatchQuery
{
    /// <summary>
    /// Must be used to add a script that does not return results to the batch
    /// </summary>
    /// <param name="builtScript">The query to translate</param>
    void AddNoResultScript(FormattableString builtScript);
    /// <summary>
    /// Method for setting up transaction lots within Runintransaction Callback.
    /// should only be called inside the callback, will launch an exception if it is called
    /// out.
    /// If the parameter limit is reached, the method will force the opening of the
    /// transaction (if you haven't done it before) and will send the accumulated batch
    /// So far, so that a new batch can be accumulated, and the transaction
    /// closed at the end.If accumulated scripts never exceed the limit of
    /// parameters, a single batch will be sent to the end of the Runintransaction callback
    /// </summary>
    /// <param name="builtScript">A query a acumular</param>
    /// <returns>A valuetask that must be awaited</returns>
    ValueTask AddTransactionScript(FormattableString builtScript);
    /// <summary>
    /// Force a accumulated transaction in Batch to already send the commands to the bank
    /// accumulated so far.ATTENTION: Does not end the transaction
    /// </summary>
    /// <returns></returns>
    ValueTask FlushTransaction();
    IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript);
    IResultHook<T> QueryFirstHook<T>(FormattableString builtScript);
    IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript);
    Task RunQueries(TimeSpan? customTimeout = null);
    Task Execute(TimeSpan? customTimeout = null);

    void Clear();

    string Sql { get; }
    int QueryCount { get; }
    int ParamCount { get; }
    IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery,
        int paramMargin = 100
    );

    /// <summary>
    /// Use the callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.Receives a reference to BatchQuery himself</param>
    /// <param name="paramMargin">Security margin for the parameter limit, that is, the script will be broken if the amount exceeds the maximum - this margin.The default is 100</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Func<IBatchQuery, ValueTask> query, int paramMargin = 100);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="paramMargin">Security margin for the parameter limit, that is, the script will be broken if the amount exceeds the maximum - this margin.The default is 100</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Func<ValueTask> query, int paramMargin = 100);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.Receives a reference to BatchQuery himself</param>
    /// <param name="paramMargin">Security margin for the parameter limit, that is, the script will be broken if the amount exceeds the maximum - this margin.The default is 100</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Action<IBatchQuery> query, int paramMargin = 100);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="options">To define safety margin and a customized command timeout</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Func<IBatchQuery, ValueTask> query, RunInTransactionOptions options);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="options">To define safety margin and a customized command timeout</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions options);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="options">To define safety margin and a customized command timeout</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task RunInTransaction(Action query, RunInTransactionOptions options);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="options">To define safety margin and a customized command timeout</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task<T> RunInTransaction<T>(Func<ValueTask<T>> query, RunInTransactionOptions? options = null);
    /// <summary>
    /// uses callback to set up a transaction that will be performed in lot with the bank.
    /// This method will break the lot in several if the number of parameters is large
    /// Too too much for the SQL driver support the command, but for that, you need
    /// the operations using the AddtransaSaSctationScript method, which will depart the commands
    /// accumulated in various scripts and send them sequentially if necessary.
    /// </summary>
    /// <param name="query">The callback that will perform the search.</param>
    /// <param name="options">To define safety margin and a customized command timeout</param>
    /// <returns>Returns a task, which must be awaited, which will perform the transaction</returns>
    Task<T> RunInTransaction<T>(Func<IBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null);

    /// <summary>
    /// cancels transaction being set up in the batch.
    /// should be called only within a runintransaction callback
    /// </summary>
    void CancelTransaction();
}