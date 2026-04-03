using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Models;
using VMManager.BLL.Configuration;
using System.Globalization;
using VMManager.BLL.Constants;
using VMManager.BLL.Interfaces.Services;
using VMManager.BLL.Mappers;

namespace VMManager.BLL.Services;

public class CsvLogger : ICsvLogger
{
    private readonly ILogger<CsvLogger> _logger;
    private readonly string _csvFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public CsvLogger(ILogger<CsvLogger> logger, IOptions<VMManagerOptions> options)
    {
        _logger = logger;
        var csvFilePath = options.Value.CsvFilePath;
        
        _csvFilePath = Path.IsPathRooted(csvFilePath) 
            ? csvFilePath 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), csvFilePath);
    }

    public async Task InitializeCsvFileAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_csvFilePath))
            {
                _logger.LogInformation("Creating new CSV file: {FilePath}", _csvFilePath);
                
                await using var writer = new StreamWriter(_csvFilePath);
                await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                
                csv.Context.RegisterClassMap<VMModelCSVMapper>();
                csv.WriteHeader<VmModel>();
                
                await csv.NextRecordAsync();
                await csv.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing CSV file");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task LogVmDataAsync(IReadOnlyCollection<VmModel> vmData, CancellationToken ct)
    {
        if (vmData.Count == 0)
        {
            _logger.LogWarning("No VM data to log");
            return;
        }

        await _fileLock.WaitAsync(ct);
        
        try
        {
            _logger.LogInformation("Logging {Count} VM records to CSV file", vmData.Count);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };

            await using var writer = new StreamWriter(_csvFilePath, append: true);
            await using var csv = new CsvWriter(writer, config);
            
            csv.Context.RegisterClassMap<VMModelCSVMapper>();

            foreach (var vm in vmData)
            {
                csv.WriteField(vm.Timestamp.ToString(CsvConstants.TimestampFormat));
                csv.WriteField(vm.SubscriptionId);
                csv.WriteField(vm.ResourceGroup);
                csv.WriteField(vm.ComputerName);
                csv.WriteField(vm.PowerState);
                csv.WriteField(vm.HasAutoShutdownTag ? VMConstants.Yes : VMConstants.No);
                csv.WriteField(vm.LastStartTime?.ToString(CsvConstants.TimestampFormat) ?? VMConstants.Unknown);
                csv.WriteField(vm.VmId);
                csv.WriteField(vm.Location);
                csv.WriteField(vm.VmSize);
        
                var tagsJson = System.Text.Json.JsonSerializer.Serialize(vm.Tags);
                csv.WriteField(tagsJson);
        
                await csv.NextRecordAsync();
            }

            await csv.FlushAsync();
            _logger.LogInformation("Successfully logged VM data to CSV file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing VM data to CSV file");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
