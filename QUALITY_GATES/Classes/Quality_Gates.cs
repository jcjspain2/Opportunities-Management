

using GrupoPremo.Intranet.Library.IService;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Models;
using SqlDataLayer;

namespace QUALITY_GATES.Classes;
public partial class Class_Projects_Quality_Gates
{
    private readonly IDbAccess _db;
    private readonly IDbAccess _dbDms;
    private  string _MODULE_ID ;
    private Module_ID _MODULE_ID_ENUM; // here was static
    private  string _ENVIRONMENT ; // Here was static
    private bool _IsUpdateInitialGate = false;
    public Class_Projects_Quality_Gates(IDbAccess db, IServiceProvider serviceProvider, Module_ID CurrentModule = Module_ID.Q_GATES, Enviroment CurrentEnviroment = Enviroment.PRODUCTION)
    {
        _db = db;
        _MODULE_ID = CurrentModule.ToString().Trim();
        _MODULE_ID_ENUM = CurrentModule;
        _ENVIRONMENT = CurrentEnviroment.ToString().Trim();

        var sqlEnv = CurrentEnviroment == Enviroment.PRODUCTION
            ? SqlDbAccess.Enviroment.PRODUCTION_ENVIROMENT
            : SqlDbAccess.Enviroment.TEST_ENVIROMENT;
        _dbDms = new SqlDbAccess(SqlDbAccess.ConnectionID.DMS, sqlEnv);

        //_dms = serviceProvider.GetService<IDmsFlowService>();
    }

    private static List<T> DeepCopyList<T>(List<T> listToCopy)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(listToCopy);
        return System.Text.Json.JsonSerializer.Deserialize<List<T>>(json)
            ?? throw new InvalidOperationException("DeepCopyList: deserialization returned null.");
    }

}
