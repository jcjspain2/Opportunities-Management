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

    public Class_Projects_Quality_Gates(IDbConnectionFactory db, IServiceProvider serviceProvider, Module_ID CurrentModule = Module_ID.Q_GATES, Enviroment CurrentEnviroment = Enviroment.PRODUCTION)
    {
        _db = db;
        _MODULE_ID = CurrentModule.ToString().Trim();
        _ENVIRONMENT = CurrentEnviroment.ToString().Trim();
        //_dms = serviceProvider.GetService<IDmsFlowService>();
    }

    public static async Task<Class_Projects_Quality_Gates> CreateAsync(
        IDbConnectionFactory db,
        IServiceProvider serviceProvider,
        Module_ID currentModule = Module_ID.Q_GATES,
        Enviroment currentEnviroment = Enviroment.PRODUCTION,
        CancellationToken cancellationToken = default)
    {
        var instance = new Class_Projects_Quality_Gates(db, serviceProvider, currentModule, currentEnviroment);
        await instance.Generate_Default_Gate_FromInitial("PCA", cancellationToken: cancellationToken);
        return instance;
    }
}
