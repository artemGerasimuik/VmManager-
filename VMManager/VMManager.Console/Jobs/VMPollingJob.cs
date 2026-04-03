using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using VMManager.BLL.Configuration;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Interfaces.Services;

namespace VMManager.Console.Jobs;

public sealed class VmPollingHostedService(
    IAzureVmService vmService,
    ICsvLogger csvLogger,
    IVmStartTimeTracker startTimeTracker,
    IOptions<VMManagerOptions> options,
    ILogger<VmPollingHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromMinutes(Math.Max(1, options.Value.PollingIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Starting VM polling cycle at {Timestamp}", DateTime.UtcNow);

                var batchSize = Math.Max(1, options.Value.VmBatchSize);
                await foreach (var vmBatch in vmService.StreamVmDataBatchesAsync(batchSize, stoppingToken))
                {
                    await csvLogger.LogVmDataAsync(vmBatch, stoppingToken);
                    await vmService.ApplyPowerManagementRulesAsync(vmBatch, stoppingToken);
                }
                await startTimeTracker.SaveStartTimesAsync(stoppingToken);

                logger.LogInformation("Completed VM polling cycle at {Timestamp}", DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during VM polling cycle");
            }

            try
            {
                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}