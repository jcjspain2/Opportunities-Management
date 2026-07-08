using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Xml.Serialization;

namespace QUALITY_GATES.Data;

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public string ConnectionStringDBProjects => _connectionString;
    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");
    }
   
    public async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<Return_SQL_Action> GetDatatableFromSelectAsync(
        string strSql,
        SqlParameter[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Return_SQL_Action();

        try
        {
            await using var conn = await CreateOpenConnectionAsync(cancellationToken);
            await using var command = new SqlCommand(strSql, conn) { CommandTimeout = 30 };

            if (parameters is { Length: > 0 })
                command.Parameters.AddRange(parameters);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var dt = new DataTable();
            dt.Load(reader);

            result.DTResults = dt;
            result.Success = true;
            result.Message = "Success";
            result.RecordsAffected = dt.Rows.Count;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
        }

        return result;
    }

    public async Task<Return_SQL_Action> NonQueryDataToSQLServer(
        string strNonQuerySQL,
        SqlParameter[]? parameters = null,
        SqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Return_SQL_Action();

        SqlConnection? ownedConnection = null;

        try
        {
            SqlConnection conn;

            if (transaction is not null)
            {
                // Usa la conexión de la transacción existente (el caller hace Commit/Rollback)
                conn = transaction.Connection
                    ?? throw new InvalidOperationException("La transacción no tiene una conexión activa.");
            }
            else
            {
                // Sin transacción: abre y gestiona su propia conexión
                ownedConnection = await CreateOpenConnectionAsync(cancellationToken);
                conn = ownedConnection;
            }

            await using var command = new SqlCommand(strNonQuerySQL, conn)
            {
                CommandTimeout = 30,
                Transaction = transaction
            };

            if (parameters is { Length: > 0 })
                command.Parameters.AddRange(parameters);

            result.RecordsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            result.Success = true;
            result.Message = "Success";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
        }
        finally
        {
            if (ownedConnection is not null)
                await ownedConnection.DisposeAsync();
        }

        return result;
    }

    public async Task<Return_SQL_Action> NonQueryDataToSQLServer(
        string strNonQuerySQL,
        ConectionDescription ID_Connection,
        SqlParameter[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Return_SQL_Action();

        try
        {
            await using var conn = new SqlConnection(ID_Connection.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(strNonQuerySQL, conn) { CommandTimeout = 30 };

            if (parameters is { Length: > 0 })
                command.Parameters.AddRange(parameters);

            result.RecordsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            result.Success = true;
            result.Message = "Success";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
        }

        return result;
    }
    public class OperationResult<T>
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public T Data { get; set; } = default!;

        // Constructores estáticos para hacer el código más legible
        public static OperationResult<T> Ok(T data)
        {
            return new OperationResult<T>
            {
                Success = true,
                ErrorMessage = string.Empty,
                Data = data
            };
        }

        public static OperationResult<T> Fail(string errorMessage)
        {
            return new OperationResult<T>
            {
                Success = false,
                ErrorMessage = errorMessage,
                Data = default!
            };
        }
    }
    List<T> IDbConnectionFactory.DeepCopyList<T>(List<T> listToCopy)
    {
        var serList = new XmlSerializer(typeof(List<T>));
        using var stringWriter = new StringWriter();
        serList.Serialize(stringWriter, listToCopy);

        using var stringReader = new StringReader(stringWriter.ToString());
        return (List<T>)(serList.Deserialize(stringReader)
            ?? throw new InvalidOperationException("DeepCopyList: deserialization returned a null value."));
    }
}
