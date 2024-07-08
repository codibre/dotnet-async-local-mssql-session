using System.Data.Common;

namespace Codibre.MSSqlSession.Impl;

internal sealed class AsyncDbStorage
{
    public DbConnection Connection { get; }
    public DbTransaction? Transaction { get; set; }
    public BatchQuery? BatchQuery { get; internal set; }

    public AsyncDbStorage(DbConnection connection) => Connection = connection;
}