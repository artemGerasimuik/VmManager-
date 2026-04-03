using VMManager.BLL.Models;

namespace VMManager.BLL.Interfaces.Services;

public interface ICsvLogger
{
    public Task LogVmDataAsync(IReadOnlyCollection<VmModel> vmData, CancellationToken ct);
    public Task InitializeCsvFileAsync(CancellationToken ct);
}
