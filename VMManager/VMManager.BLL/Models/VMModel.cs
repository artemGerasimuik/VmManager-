using Azure.ResourceManager.Compute.Models;

namespace VMManager.BLL.Models;

public class VmModel
{
    public DateTime Timestamp { get; set; }
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string PowerState { get; set; } = string.Empty;
    public bool HasAutoShutdownTag { get; set; }
    public DateTime? LastStartTime { get; set; }
    public string VmId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public VirtualMachineSizeType VmSize { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}
