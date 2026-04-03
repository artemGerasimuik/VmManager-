# Azure VM Lifecycle Manager

## Purpose
A .NET 10 console application that periodically manages and logs Azure VM state across all subscriptions in a tenant.  
Target use case: automatic stop/deallocate for VMs with `Autoshutdown=1`, with append-only CSV audit logging.

## Key Features & Architecture

- **Manual Scheduler (BackgroundService):**
  - Uses a hosted background worker (`VmPollingHostedService`) with a configurable interval (`PollingIntervalMinutes`).
  - The worker runs a polling cycle, then delays until the next cycle (`Task.Delay`) until the app is stopped.

- **File Write Synchronization:**
  - CSV writes are protected by `SemaphoreSlim` to avoid concurrent file corruption.

- **VM Start Time Tracking:**
  - VM start time (for tagged VMs) is tracked in memory with `ConcurrentDictionary` and persisted to JSON.
  - Persisted state is restored on startup to preserve the 8-hour rule across restarts.

- **Services & Layers:**
  - **Console/Jobs:** App entrypoint and recurring orchestration.
  - **AzureVmService:** VM discovery, batch streaming, throttled parallelism, and power-management actions.
  - **CsvLogger:** Append-only CSV writer for VM records.
  - **VmStartTimeTracker:** Memory + file-backed store for VM start times.

- **Configuration:**
  - All runtime behavior is configurable via `VMManager.Console/appsettings.json`.
  - Main options:
    - `PollingIntervalMinutes`
    - `ShutdownThresholdHours`
    - `MaxParallelOperations`
    - `VmBatchSize`
    - `RetryMaxAttempts`
    - `RetryBaseDelayMs`

- **Authentication:**
  - Uses `DefaultAzureCredential` (developer login / managed identity).

- **Parallel & Async:**
  - VM discovery is asynchronous and runs with bounded concurrency (`MaxParallelOperations`).
  - Processing is stream-based (`IAsyncEnumerable` batches) to reduce peak memory usage on large tenants.
  - Azure operations support `CancellationToken` for graceful shutdown.

- **Retry / Backoff for Azure API:**
  - Centralized retry wrapper for transient Azure failures (`429`, `500`, `502`, `503`, `504`).
  - Exponential backoff with jitter is applied to key ARM calls (inventory/power operations).

- **API Call Optimization:**
  - `InstanceView` is requested only for VMs with `Autoshutdown=1`.
  - Power operations use direct VM resource identifier access to avoid extra lookup calls.

## How to Use
1. Install [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
2. Configure `VMManager.Console/appsettings.json`.
3. Authenticate to Azure (e.g., `az login` or Managed Identity in cloud).
4. Run the console app: `dotnet run --project VMManager/VMManager.Console`.
5. Review output files (CSV inventory log + JSON start-time state), stored by configured paths.

## Additional Notes
- The app is designed to run indefinitely until manually stopped.
- Every poll appends new records to CSV (append-only behavior).
- Errors are logged and handled per operation so one failure does not terminate the process.
