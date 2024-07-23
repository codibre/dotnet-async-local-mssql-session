using Microsoft.Data.SqlClient;

namespace Codibre.MSSqlSession.Impl;

public class AsyncSqlOptions
{
    public string ConnectionString { get; set; }
    public bool CustomPool { get; set; }
    public SqlRetryLogicBaseProvider? RetryLogicBaseProvider { get; set; }
}