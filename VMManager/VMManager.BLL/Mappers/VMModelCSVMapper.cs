using CsvHelper.Configuration;
using VMManager.BLL.Constants;
using VMManager.BLL.Models;

namespace VMManager.BLL.Mappers;

public sealed class VMModelCSVMapper : ClassMap<VmModel>
{
    public VMModelCSVMapper()
    {
        Map(m => m.Timestamp).Name(CsvConstants.TimestampHeader);
        Map(m => m.SubscriptionId).Name(CsvConstants.SubscriptionIdHeader);
        Map(m => m.ResourceGroup).Name(CsvConstants.ResourceGroupHeader);
        Map(m => m.ComputerName).Name(CsvConstants.ComputerNameHeader);
        Map(m => m.PowerState).Name(CsvConstants.PowerStateHeader);
        Map(m => m.HasAutoShutdownTag).Name(CsvConstants.HasAutoShutdownTagHeader);
        Map(m => m.LastStartTime).Name(CsvConstants.LastStartTimeHeader);
        Map(m => m.VmId).Name(CsvConstants.VmIdHeader);
        Map(m => m.Location).Name(CsvConstants.LocationHeader);
        Map(m => m.VmSize).Name(CsvConstants.VmSizeHeader);
        Map(m => m.Tags).Name(CsvConstants.TagsHeader);
    }
}

