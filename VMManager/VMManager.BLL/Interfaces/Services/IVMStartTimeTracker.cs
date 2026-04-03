namespace VMManager.BLL.Interfaces;

public interface IVmStartTimeTracker
{
    public Task LoadStartTimesAsync(CancellationToken ct);
    public Task SaveStartTimesAsync(CancellationToken ct);
    public void UpdateVmStartTime(string vmId, string powerState, DateTimeOffset? time);
    public DateTime? GetVmStartTime(string vmId);
    public bool ShouldShutdownVm(string vmId, int thresholdHours = 8);
}