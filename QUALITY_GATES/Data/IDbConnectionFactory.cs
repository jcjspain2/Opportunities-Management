using Microsoft.Data.SqlClient;
using System.Data;

namespace QUALITY_GATES.Data;

public interface IDbConnectionFactory
{
    Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

    public string ConnectionStringDBProjects { get; }
    Task<Return_SQL_Action> GetDatatableFromSelectAsync(
        string strSql,
        SqlParameter[]? parameters = null,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Executes a non-query SQL command (INSERT, UPDATE, DELETE) against the SQL Server database, within a given transaction      
    /// </summary>
    /// <param name="strNonQuerySQL">SQL with transaction to execute</param>
    /// <param name="parameters">List of parameters if provided</param>
    /// <param name="transaction">Transaction from callet function</param>
    /// <param name="cancellationToken">Token for cancelling sql</param>
    /// <returns>Return SQL Action object</returns>
    Task<Return_SQL_Action> NonQueryDataToSQLServer(
        string strNonQuerySQL,
        SqlParameter[]? parameters = null,
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

