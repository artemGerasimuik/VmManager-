using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using VMManager.BLL.Configuration;
using VMManager.BLL.Constants;
using VMManager.BLL.Interfaces;
using VMManager.BLL.Interfaces.Services;
using VMManager.BLL.Models;

namespace VMManager.BLL.Services;

public class AzureVmService : IAzureVmService
{
    private readonly ArmClient _armClient;
    private readonly ILogger<AzureVmService> _logger;
    private readonly IVmStartTimeTracker _startTimeTracker;
    private readonly int _shutdownThresholdHours;
    private readonly int _maxParallelOperations;
    private readonly int _retryMaxAttempts;
    private readonly int _retryBaseDelayMs;

    public AzureVmService(
        ILogger<AzureVmService> logger,
        IVmStartTimeTracker startTimeTracker,
        IOptions<VMManagerOptions> options)
    {
        _logger = logger;
        _startTimeTracker = startTimeTracker;
        _shutdownThresholdHours = Math.Max(1, options.Value.ShutdownThresholdHours);
        _maxParallelOperations = Math.Max(1, options.Value.MaxParallelOperations);
        _retryMaxAttempts = Math.Max(1, options.Value.RetryMaxAttempts);
        _retryBaseDelayMs = Math.Max(100, options.Value.RetryBaseDelayMs);

        var credential = new DefaultAzureCredential();
        _armClient = new ArmClient(credential);
    }

    public async IAsyncEnumerable<IReadOnlyList<VmModel>> StreamVmDataBatchesAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var normalizedBatchSize = Math.Max(1, batchSize);
        var currentBatch = new List<VmModel>(normalizedBatchSize);
        var timestamp = DateTime.UtcNow;
        List<SubscriptionResource> subscriptions;

        _logger.LogInformation("Starting collection of VM data from all subscriptions");
        try
        {
            subscriptions = await ExecuteWithRetryAsync(async token =>
            {
                var subscriptionResources = new List<SubscriptionResource>();
                await foreach (var subscription in _armClient.GetSubscriptions().WithCancellation(token))
                {
                    subscriptionResources.Add(subscription);
                }

                return subscriptionResources;
            }, "list subscriptions", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while collecting VM data");
            throw;
        }

        _logger.LogInformation("Found {Count} subscriptions", subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            await foreach (var vmData in CollectVmDataFromSubscriptionAsync(subscription, timestamp, ct))
            {
                currentBatch.Add(vmData);
                if (currentBatch.Count < normalizedBatchSize)
                {
                    continue;
                }

                yield return currentBatch.ToList();
                currentBatch.Clear();
            }
        }

        if (currentBatch.Count > 0)
        {
            yield return currentBatch.ToList();
        }
    }

    private async IAsyncEnumerable<VmModel> CollectVmDataFromSubscriptionAsync(
        SubscriptionResource subscription,
        DateTime timestamp,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_maxParallelOperations, _maxParallelOperations);
        var tasks = new List<Task<List<VmModel>>>();

        try
        {
            _logger.LogDebug("Collecting VM data from subscription: {SubscriptionId}", subscription.Id);

            var resourceGroups = subscription.GetResourceGroups();

            await foreach (var resourceGroup in resourceGroups)
            {
                tasks.Add(CollectVmDataFromResourceGroupWithThrottleAsync(subscription, resourceGroup, timestamp, semaphore, ct));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting VM data from subscription {SubscriptionId}", subscription.Id);
            yield break;
        }

        while (tasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(tasks);
            tasks.Remove(completedTask);

            foreach (var vmData in await completedTask)
            {
                yield return vmData;
            }
        }
    }

    private async Task<List<VmModel>> CollectVmDataFromResourceGroupWithThrottleAsync(
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        DateTime timestamp,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            return await ExecuteWithRetryAsync(
                token => CollectVmDataFromResourceGroupCoreAsync(subscription, resourceGroup, timestamp, token),
                $"collect VMs from resource group {resourceGroup.Data.Name}",
                ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<List<VmModel>> CollectVmDataFromResourceGroupCoreAsync(
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        DateTime timestamp,
        CancellationToken ct)
    {
        var vmDataList = new List<VmModel>();

        try
        {
            var vms = resourceGroup.GetVirtualMachines();

            await foreach (var vm in vms.GetAllAsync(cancellationToken: ct))
            {
                try
                {
                    var vmData = await CreateVmDataAsync(vm, subscription, resourceGroup, timestamp, ct);
                    vmDataList.Add(vmData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing VM {VmName} in resource group {ResourceGroup}",
                        vm.Data.Name, resourceGroup.Data.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting VMs from resource group {ResourceGroup}",
                resourceGroup.Data.Name);
        }

        return vmDataList;
    }

    private async Task<VmModel> CreateVmDataAsync(
        VirtualMachineResource vm,
        SubscriptionResource subscription,
        ResourceGroupResource resourceGroup,
        DateTime timestamp,
        CancellationToken ct)
    {
        var vmData = new VmModel
        {
            Timestamp = timestamp,
            SubscriptionId = subscription.Id.SubscriptionId
                             ?? throw new ArgumentNullException(subscription.Id.SubscriptionId),
            ResourceGroup = resourceGroup.Data.Name,
            ComputerName = vm.Data.Name,
            VmId = vm.Id.ToString(),
            Location = vm.Data.Location.Name,
            VmSize = vm.Data.HardwareProfile?.VmSize ?? VMConstants.Unknown,
            Tags = vm.Data.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new Dictionary<string, string>(),
            HasAutoShutdownTag = vm.Data.Tags?.ContainsKey(VMConstants.AutoShutdown) == true &&
                                 vm.Data.Tags[VMConstants.AutoShutdown] == "1"
        };

        if (!vmData.HasAutoShutdownTag)
        {
            vmData.PowerState = VMConstants.Unknown;
            return vmData;
        }

        try
        {
            var data = await ExecuteWithRetryAsync(
                vm.InstanceViewAsync,
                $"get instance view for VM {vm.Data.Name}",
                ct);
            var vmPowerStateAndTime = GetPowerStateAndTimeFromInstanceView(data);
            vmData.PowerState = vmPowerStateAndTime.PowerState;

            _startTimeTracker.UpdateVmStartTime(vmData.VmId, vmData.PowerState, vmPowerStateAndTime.PowerStateTime);

            vmData.LastStartTime = _startTimeTracker.GetVmStartTime(vmData.VmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve instance view for VM {VmName}", vm.Data.Name);
            vmData.PowerState = VMConstants.Unknown;
        }

        return vmData;
    }

    private static PowerStateModel GetPowerStateAndTimeFromInstanceView(
        VirtualMachineInstanceView instanceView)
    {
        var statuses = instanceView.Statuses;
        if (statuses == null)
        {
            return new PowerStateModel()
            {
                PowerState = VMConstants.Unknown,
                PowerStateTime = null
            };
        }

        foreach (var status in statuses)
        {
            if (status.Code?.StartsWith(VMConstants.PowerState) == true)
            {
                return new PowerStateModel()
                {
                    PowerState = status.Code.Replace(VMConstants.PowerState, string.Empty),
                    PowerStateTime = status.Time
                };
            }
        }

        return new PowerStateModel
        {
            PowerState = VMConstants.Unknown,
            PowerStateTime = null
        };
    }

    public async Task ApplyPowerManagementRulesAsync(
        IReadOnlyCollection<VmModel> vmData,
        CancellationToken ct)
    {
        var autoshutdownVMs = vmData.Where(vm => vm.HasAutoShutdownTag).ToList();

        if (autoshutdownVMs.Count == 0)
        {
            _logger.LogInformation("No VMs with Autoshutdown tag found");
            return;
        }

        _logger.LogInformation("Applying power management rules to {Count} VMs with Autoshutdown tag",
            autoshutdownVMs.Count);

        using var semaphore = new SemaphoreSlim(_maxParallelOperations, _maxParallelOperations);
        var tasks = autoshutdownVMs.Select(async vm =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ApplyPowerManagementRuleToVmAsync(vm, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private async Task ApplyPowerManagementRuleToVmAsync(
        VmModel vm,
        CancellationToken ct)
    {
        try
        {
            switch (vm.PowerState.ToLowerInvariant())
            {
                case VMConstants.RunningState:
                    if (_startTimeTracker.ShouldShutdownVm(vm.VmId, _shutdownThresholdHours))
                    {
                        _logger.LogInformation("Shutting down VM {VmName} - running for more than 8 hours",
                            vm.ComputerName);
                        await ShutdownVmByIdAsync(vm.VmId, vm.ComputerName, ct);
                    }

                    break;

                case VMConstants.StoppedState:
                    _logger.LogInformation("Deallocating VM {VmName} - Windows is shutdown",
                        vm.ComputerName);
                    await DeallocateVmByIdAsync(vm.VmId, vm.ComputerName, ct);
                    break;

                case VMConstants.DeallocatedState:
                    _logger.LogDebug("VM {VmName} is already deallocated", vm.ComputerName);
                    break;

                default:
                    _logger.LogDebug("VM {VmName} is in state {PowerState} - no action needed",
                        vm.ComputerName, vm.PowerState);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying power management rule to VM {VmName}", vm.ComputerName);
        }
    }


    private async Task ShutdownVmByIdAsync(
        string vmId,
        string vmName,
        CancellationToken ct)
    {
        try
        {
            var vmResource = _armClient.GetVirtualMachineResource(new ResourceIdentifier(vmId));
            await ExecuteWithRetryAsync(
                token => vmResource.PowerOffAsync(WaitUntil.Completed, cancellationToken: token),
                $"power off VM {vmName}",
                ct);
            _startTimeTracker.UpdateVmStartTime(vmId, VMConstants.StoppedState, DateTimeOffset.UtcNow);
            _logger.LogInformation("Successfully shut down VM {VmName}", vmName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error shutting down VM {VmName}", vmName);
        }
    }

    private async Task DeallocateVmByIdAsync(
        string vmId,
        string vmName,
        CancellationToken ct)
    {
        try
        {
            var vmResource = _armClient.GetVirtualMachineResource(new ResourceIdentifier(vmId));
            await ExecuteWithRetryAsync(
                token => vmResource.DeallocateAsync(WaitUntil.Completed, cancellationToken: token),
                $"deallocate VM {vmName}",
                ct);
            _startTimeTracker.UpdateVmStartTime(vmId, VMConstants.DeallocatedState, DateTimeOffset.UtcNow);
            _logger.LogInformation("Successfully deallocated VM {VmName}", vmName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deallocating VM {VmName}", vmName);
        }
    }

    public async Task ShutdownVmAsync(
        string subscriptionId,
        string resourceGroup,
        string vmName,
        CancellationToken ct)
    {
        try
        {
            var vmResourceId = VirtualMachineResource.CreateResourceIdentifier(subscriptionId, resourceGroup, vmName);
            await ShutdownVmByIdAsync(vmResourceId.ToString(), vmName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error shutting down VM {VmName}", vmName);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 429 || ex.Status == 500 || ex.Status == 502 || ex.Status == 503 || ex.Status == 504 && attempt < _retryMaxAttempts)
            {
                var exponentialMs = _retryBaseDelayMs * Math.Pow(2, attempt - 1);
                var jitterMs = Random.Shared.Next(100, 301);
                var delay =  TimeSpan.FromMilliseconds(exponentialMs + jitterMs);

                _logger.LogWarning(ex,
                    "Transient Azure error during {Operation}. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs} ms",
                    operationName, attempt, _retryMaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }
}