using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Classes;
using SqlDataLayer;
using SqlDataLayer.Extensions;

namespace QUALITY_GATES;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Quality Gates services. Requires "QualityGatesConnection" (Intranet app)
    /// or "DefaultConnection" (standalone) in IConfiguration.
    /// The consuming app must also register IDmsFlowService before calling this method,
    /// e.g. via services.AddIntranetLibrary(...) from GrupoPremo.Intranet.Library.
    /// </summary>
    public static IServiceCollection AddQualityGatesData(this IServiceCollection services)
    {
        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<IConfiguration>();

        var connStr = config.GetConnectionString("QualityGatesConnection")
                   ?? config.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException(
                          "No connection string found. Add 'QualityGatesConnection' or 'DefaultConnection' to appsettings.");

        services.AddSqlDbAccess(connStr);
        services.AddScoped<Class_Projects_Quality_Gates>();
        return services;
    }
}
