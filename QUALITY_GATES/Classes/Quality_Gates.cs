

using GrupoPremo.Intranet.Library.IService;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;

namespace QUALITY_GATES.Classes;
public partial class Class_Projects_Quality_Gates
{
    private readonly IDbConnectionFactory _db;
    private  string _MODULE_ID ;
    private Module_ID _MODULE_ID_ENUM; // here was static
    private  string _ENVIRONMENT ; // Here was static
    private bool _IsUpdateInitialGate = false;
    public Class_Projects_Quality_Gates(IDbConnectionFactory db, IServiceProvider serviceProvider, Module_ID CurrentModule = Module_ID.Q_GATES, Enviroment CurrentEnviroment = Enviroment.PRODUCTION)
    {

        _db = db;
        _MODULE_ID = CurrentModule.ToString().Trim();
        _MODULE_ID_ENUM = CurrentModule;
        _ENVIRONMENT = CurrentEnviroment.ToString().Trim();
        
        //_dms = serviceProvider.GetService<IDmsFlowService>();
    }


}
