using Microsoft.Data.SqlClient;

namespace QUALITY_GATES.Data;

public class ConectionDescription
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int ConnectionTimeout { get; set; } = 60;

    internal string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            UserID = UserId,
            Password = Password,
            ConnectTimeout = ConnectionTimeout,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }
}
