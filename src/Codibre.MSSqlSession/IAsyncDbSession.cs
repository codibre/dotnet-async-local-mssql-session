using System.Data.Common;

namespace Codibre.MSSqlSession;

public interface IAsyncDbSession : IDisposable
{
    DbConnection Connection { get; }
    DbTransaction? Transaction { get; set; }
    IBatchQuery BatchQuery { get; }

    bool ConnectionAcquired { get; }
    Task Run(Func<Task> actions);
    Task<T> Run<T>(Func<Task<T>> actions);
    Task<IDisposable> StartTransaction();

    Task Commit();

    Task Rollback();
    Task<IDisposable> StartSession();
    IScriptBuilder CreateScriptBuilder();
}