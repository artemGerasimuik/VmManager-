using Microsoft.Extensions.DependencyInjection;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Interfaces.Services;
using VMManager.BLL.Services;

namespace VMManager.BLL.DI;

public static class BllConfiguration
{
    public static void AddBllDependencies(this IServiceCollection services)
    {
        services.AddSingleton<IVmStartTimeTracker, VmStartTimeTracker>();
        services.AddScoped<IAzureVmService, AzureVmService>();
        services.AddScoped<ICsvLogger, CsvLogger>();
    }
}