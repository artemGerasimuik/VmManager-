using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using VMManager.Console.Jobs;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Configuration;
using VMManager.BLL.DI;
using VMManager.BLL.Interfaces.Services;
using VMManager.Console.Constants;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<VMManagerOptions>(builder.Configuration.GetSection(VMManagerOptions.SectionName));
var vmOptions = builder.Configuration.GetSection(VMManagerOptions.SectionName).Get<VMManagerOptions>() ?? new VMManagerOptions();
var pollingIntervalMinutes = Math.Max(1, vmOptions.PollingIntervalMinutes);
    
var ct = CancellationToken.None;
ct.ThrowIfCancellationRequested();

builder.Services.AddBllDependencies();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey(JobConstants.PollingJobKey);
    
    q.AddJob<VmPollingJob>(opts => opts.WithIdentity(jobKey));
    
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity(JobConstants.PollingJobTrigger)
        .WithSimpleSchedule(x => x
            .WithInterval(TimeSpan.FromMinutes(pollingIntervalMinutes))
            .RepeatForever())
        .StartNow());
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var csvLogger = scope.ServiceProvider.GetRequiredService<ICsvLogger>();
    var startTimeTracker = scope.ServiceProvider.GetRequiredService<IVmStartTimeTracker>();
    
    await csvLogger.InitializeCsvFileAsync(ct);
    await startTimeTracker.LoadStartTimesAsync(ct);
}

await host.RunAsync();