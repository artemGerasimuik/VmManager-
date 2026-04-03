using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VMManager.BLL.Configuration;
using VMManager.BLL.Constants;
using VMManager.BLL.Interfaces;

namespace VMManager.BLL.Services;

public class VmStartTimeTracker : IVmStartTimeTracker
{
    private readonly ILogger<IVmStartTimeTracker> _logger;
    private readonly string _trackingFilePath;
    private readonly ConcurrentDictionary<string, DateTime> _vmStartTimes = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public VmStartTimeTracker(ILogger<IVmStartTimeTracker> logger, IOptions<VMManagerOptions> options)
    {
        _logger = logger;
        var trackingFilePath = options.Value.TrackingFilePath;
        _trackingFilePath = Path.IsPathRooted(trackingFilePath) 
            ? trackingFilePath 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), trackingFilePath);
        
        _logger.LogInformation("VM start time tracking file will be created at: {FilePath}", _trackingFilePath);
    }

    public async Task LoadStartTimesAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (File.Exists(_trackingFilePath))
            {
                var json = await File.ReadAllTextAsync(_trackingFilePath, ct);
                var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
                if (data != null)
                {
                    _vmStartTimes.Clear();
                    foreach (var kvp in data)
                    {
                        _vmStartTimes[kvp.Key] = kvp.Value;
                    }
                    _logger.LogInformation("Loaded {Count} VM start times from tracking file", _vmStartTimes.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading VM start times from file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveStartTimesAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var snapshot = _vmStartTimes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_trackingFilePath, json, ct);
            _logger.LogDebug("Saved {Count} VM start times to tracking file", _vmStartTimes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving VM start times to file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void UpdateVmStartTime(string vmId, string powerState, DateTimeOffset? time)
    {
        var stateTime = time?.UtcDateTime;
        var now = DateTime.UtcNow;
        
        if (powerState.Equals(VMConstants.RunningState, StringComparison.OrdinalIgnoreCase))
        {
            var candidateStartTime = stateTime ?? now;
            var hadExistingValue = _vmStartTimes.TryGetValue(vmId, out var existingStartTime);
            _vmStartTimes[vmId] = candidateStartTime;

            if (!hadExistingValue)
            {
                _logger.LogDebug("Started tracking VM {VmId} at {StartTime}", vmId, candidateStartTime);
                return;
            }

            if (existingStartTime != candidateStartTime)
            {
                _logger.LogDebug(
                    "Updated tracked start time for VM {VmId} from {OldStartTime} to {NewStartTime}",
                    vmId,
                    existingStartTime,
                    candidateStartTime);
            }
        }
        else
        {
            if (!_vmStartTimes.Remove(vmId, out _)) return;
            
            _logger.LogDebug("Stopped tracking VM {VmId}", vmId);
        }
    }

    public DateTime? GetVmStartTime(string vmId)
    {
        return _vmStartTimes.TryGetValue(vmId, out var startTime) ? startTime : null;
    }

    public bool ShouldShutdownVm(string vmId, int thresholdHours = 8)
    {
        var startTime = GetVmStartTime(vmId);
        if (startTime is null) return false;

        var runningTime = DateTime.UtcNow - startTime.Value;
        return runningTime.TotalHours >= thresholdHours;
    }
}
