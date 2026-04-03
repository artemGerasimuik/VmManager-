using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VMManager.Console.Jobs;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Configuration;
using VMManager.BLL.DI;
using VMManager.BLL.Interfaces.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<VMManagerOptions>(builder.Configuration.GetSection(VMManagerOptions.SectionName));
    
var ct = CancellationToken.None;

builder.Services.AddBllDependencies();
builder.Services.AddHostedService<VmPollingHostedService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var csvLogger = scope.ServiceProvider.GetRequiredService<ICsvLogger>();
    var startTimeTracker = scope.ServiceProvider.GetRequiredService<IVmStartTimeTracker>();
    
    await csvLogger.InitializeCsvFileAsync(ct);
    await startTimeTracker.LoadStartTimesAsync(ct);
}

await host.RunAsync();