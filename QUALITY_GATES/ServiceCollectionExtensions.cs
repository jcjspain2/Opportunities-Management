using Microsoft.Extensions.DependencyInjection;
using QUALITY_GATES.Classes;
using QUALITY_GATES.Data;

namespace QUALITY_GATES;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Quality Gates services. Requires "DefaultConnection" in IConfiguration.
    /// </summary>
    public static IServiceCollection AddQualityGatesData(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<Class_Projects_Quality_Gates>();
        return services;
    }
}
