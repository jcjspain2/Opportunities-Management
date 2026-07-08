using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QUALITY_GATES;
using QUALITY_GATES.Classes;

namespace Tester_Quality_Gates;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.json", optional: false);
                config.AddJsonFile("appsettings.local.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices(services =>
            {
                services.AddQualityGatesData();   // registra IDbConnectionFactory + Class_Projectos
                services.AddTransient<Form1>();
            })
            .Build();

        Application.Run(host.Services.GetRequiredService<Form1>());
    }
}
