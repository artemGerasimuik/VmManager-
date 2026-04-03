namespace VMManager.BLL.Models;

public class PowerStateModel
{
    public string PowerState { get; set; } = string.Empty;
    public DateTimeOffset? PowerStateTime { get; set; }
}