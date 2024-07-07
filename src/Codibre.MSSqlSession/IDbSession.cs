using System.Data;

namespace Codibre.MSSqlSession;

public interface IDbSession : IDisposable
{
    IDbConnection Connection { get; }

    IDbTransaction? Transaction { get; set; }

    void Commit();

    void Rollback();
}