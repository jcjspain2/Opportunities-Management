namespace QUALITY_GATES.Classes;

using GrupoPremo.Intranet.Library.IService;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Data;

public partial class Class_Projects_Quality_Gates
{
    private readonly IDbConnectionFactory _db;
    private readonly IDmsFlowService? _dms;

    public Class_Projects_Quality_Gates(IDbConnectionFactory db, IServiceProvider serviceProvider)
    {
        _db = db;
        //_dms = serviceProvider.GetService<IDmsFlowService>();
    }
}
