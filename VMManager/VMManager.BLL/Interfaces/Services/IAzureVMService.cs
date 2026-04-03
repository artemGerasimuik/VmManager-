using VMManager.BLL.Models;

namespace VMManager.BLL.Interfaces.Services;

public interface IAzureVmService
{
    public IAsyncEnumerable<IReadOnlyList<VmModel>> StreamVmDataBatchesAsync(int batchSize, CancellationToken ct);
    public Task ApplyPowerManagementRulesAsync(IReadOnlyCollection<VmModel> vmData, CancellationToken ct);
}
