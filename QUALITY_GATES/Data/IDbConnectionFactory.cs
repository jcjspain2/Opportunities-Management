using Microsoft.Data.SqlClient;
using System.Data;

namespace QUALITY_GATES.Data;

public enum DatabaseTarget { SRM, DMS }

public interface IDbConnectionFactory
{
    Task<SqlConnection> CreateOpenConnectionAsync(DatabaseTarget db = DatabaseTarget.SRM, CancellationToken cancellationToken = default);

    public string ConnectionStringDBProjects { get; }

    Task<Return_SQL_Action> GetDatatableFromSelectAsync(
        string strSql,
        SqlParameter[]? parameters = null,
        DatabaseTarget db = DatabaseTarget.SRM,
        CancellationToken cancellationToken = default);

    Task<Return_SQL_Action> NonQueryDataToSQLServer(
        string strNonQuerySQL,
        SqlParameter[]? parameters = null,
        DatabaseTarget db = DatabaseTarget.SRM,
        SqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<Return_SQL_Action> NonQueryDataToSQLServer(
        string strNonQuerySQL,
        ConectionDescription ID_Connection,
        SqlParameter[]? parameters = null,
        CancellationToken cancellationToken = default);

    internal List<T> DeepCopyList<T>(List<T> listToCopy);
}

public class Return_SQL_Action
{
    public bool Success { get; set; } = false;
    public string Message { get; set; } = string.Empty;
    public int RecordsAffected { get; set; } = 0;
    public DataTable? DTResults { get; set; } = null;
}
