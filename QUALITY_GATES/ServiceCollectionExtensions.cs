using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Classes;
using QUALITY_GATES.Data;

namespace QUALITY_GATES;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Quality Gates services. Requires "DefaultConnection" in IConfiguration.
    /// The consuming app must also register IDmsFlowService before calling this method,
    /// e.g. via services.AddIntranetLibrary(...) from GrupoPremo.Intranet.Library.
    /// </summary>
    public static IServiceCollection AddQualityGatesData(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<Class_Projects_Quality_Gates>();
        return services;
    }
}
