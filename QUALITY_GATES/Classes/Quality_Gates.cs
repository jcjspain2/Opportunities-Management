namespace QUALITY_GATES.Classes;

using GrupoPremo.Intranet.Library.IService;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;

public partial class Class_Projects_Quality_Gates
{
    private readonly IDbConnectionFactory _db;
    private readonly IDmsFlowService? _dms;
    static private  string _MODULE_ID ;
    private  string _ENVIRONMENT ;

    public Class_Projects_Quality_Gates(IDbConnectionFactory db, IServiceProvider serviceProvider,Module_ID CurrentModule = Module_ID.Q_GATES, Enviroment CurrentEnviroment = Enviroment.PRODUCTION)
    {
        _db = db;
       _MODULE_ID = CurrentModule.ToString().Trim(); // defines wich module ID we are going to work with
       _ENVIRONMENT = CurrentEnviroment.ToString().Trim(); // defines wich environment we are going to work with
        //_dms = serviceProvider.GetService<IDmsFlowService>();
    }
}
