# Codibre.MSSqlSession

Library for SqlClient connections using AsyncLocal for management


## Why?

When used with Dapper, DBConnection management requires more manual management and connection closure, if we want to minimize the number of obtaining and returns from a pool connection.Of course, Dapper himself tries to do this work if the connection is closed, but we always need to worry about it when we want to make transactions, for example, and it is important to maintain the type of connection injection (if we are working with dependence injection) As a Request Scoped, otherwise, or we use many connections per request, or we use the same connection in various Request, which causes problems.

For all this, we believe that injecting a session manager like Singleton and abstracting the obtaining of these connections using AsyncLocal is the best way. AsyncLocal brings additional overload, but can make connection management much simpler, along with transaction management, and also provides a separation of responsibilities between simpler repositories, as we do not need to pass a transaction for all methods by parameter.They want to use it, and just leave the AsyncLocal session manager to take care of it.


## How to use?

First, inject the asynchronous connection manager:

```c#
services.AddAsyncDb();
```

Now inject the **IAsyncDbSession** everywhere you need, for example, in repository:

```c#
public class MyRepository(IAsyncDbSession session): IMyRepository {
    private Readonly _session_ = session;
    public Task<IEnumerable<Person>> GetByName(string name)
    => _session.Connection.QueryAsync<Person>(@"Select *
        From Person
        Where Name Like '%@name%' "
        new { name }
        );
    }
```

If you want to make multiple requests using the same connection obtained from the pool, use ** StartSession **:

```c#
using (await _session.StartSession()) {
    var people = await _personRepository.GetByName(name);
    var jobs = await _personRepository.GetJobs(jobType);
}
```

To do transactional operations:

```c#
using (await _session.StartSession()) {
    try {
        await _session.BeginTransaction();
        await _personRepository.Save(Person);
        await _personRepository.Save(JOB);
        await _Session.Commit();
    } catch {
        await _Session.Rollback();
        throw;
    }
}
```

## BatchQuery and QueryHooks

IAsyncDbSession.QueryBatch can be used to minimize the number of round trips with the database by using **QueryHooks**. These are placeholders which will be a way to access a query result after it is triggered, like in the example below:

```c#
var peopleHook = _personRepository.GetByName(name);
var jobsHook = _personRepository.GetJobs(jobType);

await _session.RunQueries();

var people = peopleHook.Result; // Will Return the query result of GetByName
var jobs = jobsHook.Result; // Will return the query result of GetJobs
```

To achieve that, however, **BatchQuery** must be used in the repository:


```c#
public class MyRepository(IAsyncDbSession session): IMyRepository {
    private Readonly _session_ = session;
    public IResultHook<IEnumerable<Person>> GetByName(string name)
    => _session.BatchQuery.QueryHook<Person>($@"Select *
        From Person
        Where Name Like '%@{name}%'");
    }
```

If **.Result** is accessed before **RunQueries** call, an error will be thrown, as the query hasn't been executed yet.

## RunInTransaction

BatchQuery also offers an option to run, in a single RoundTrip, a full transaction, following the example below:

```c#
_session.BatchQuery.RunInTransaction(async () => {
    await _session.BatchQuery.AddTransactionScript($"INSERT ... < SOME SQL INSTRUCTION ... >");
    await _session.BatchQuery.AddTransactionScript($"INSERT ... < SOME OTHER SQL INSTRUCTION ... >");
    foreach (var item in items) {
        await _session.BatchQuery.AddTransactionScript($"INSERT ... < SOME OTHER SQL INSTRUCTION using {item}... >");
    }
});
```

If the transaction becomes too large to be fit in a single call, then it will be divided in multiple round trips. You can also force part of it to be executed, if needed, using **FlushTransaction**:

```c#
await _session.BatchQuery.FlushTransaction();
```