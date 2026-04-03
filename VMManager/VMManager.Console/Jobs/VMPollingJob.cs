using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using VMManager.BLL.Configuration;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Interfaces.Services;

namespace VMManager.Console.Jobs;

public class VmPollingJob(
    IAzureVmService vmService,
    ICsvLogger csvLogger,
    IVmStartTimeTracker startTimeTracker,
    IOptions<VMManagerOptions> options,
    ILogger<VmPollingJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            logger.LogInformation("Starting VM polling cycle at {Timestamp}", DateTime.UtcNow);

            var ct = context.CancellationToken;
            ct.ThrowIfCancellationRequested();

            var batchSize = Math.Max(1, options.Value.VmBatchSize);
            await foreach (var vmBatch in vmService.StreamVmDataBatchesAsync(batchSize, ct))
            {
                await csvLogger.LogVmDataAsync(vmBatch, ct);
                await vmService.ApplyPowerManagementRulesAsync(vmBatch, ct);
            }
            await startTimeTracker.SaveStartTimesAsync(ct);
            
            logger.LogInformation("Completed VM polling cycle at {Timestamp}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during VM polling cycle");
        }
    }
}