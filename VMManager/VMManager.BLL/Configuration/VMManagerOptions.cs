namespace VMManager.BLL.Configuration;

public class VMManagerOptions
{
    public const string SectionName = "VMManager";

    public string CsvFilePath { get; set; } = string.Empty;
    public string TrackingFilePath { get; set; } = string.Empty;
    public int PollingIntervalMinutes { get; set; } = 5;
    public int ShutdownThresholdHours { get; set; } = 8;
    public int MaxParallelOperations { get; set; } = 10;
    public int VmBatchSize { get; set; } = 100;
    public int RetryMaxAttempts { get; set; } = 4;
    public int RetryBaseDelayMs { get; set; } = 500;
}
