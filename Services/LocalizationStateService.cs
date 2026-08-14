using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationStateService
{
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;

    public LocalizationStateService(InstallationStateStore stateStore, ILogger logger)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LocalizationState> ResolveAsync(
        CurrentRelease? current,
        string gameLocFilePath,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Load installation state
        var loadResult = _stateStore.Load();

        if (loadResult.Status == FileLoadStatus.Missing)
        {
            _logger.Debug("State resolution: NotInstalled (metadata missing)");
            return LocalizationState.NotInstalled;
        }

        if (loadResult.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning($"State resolution: InstalledVersionUnknown (metadata invalid: {loadResult.Error})");
            return LocalizationState.InstalledVersionUnknown;
        }

        var metadata = loadResult.Value!;

        if (metadata.Source == "official")
        {
            _logger.Debug("State resolution: NotInstalled (source=official)");
            return LocalizationState.NotInstalled;
        }

        // Step 2: API-installed — verify actual file
        if (!File.Exists(gameLocFilePath))
        {
            _logger.Error($"State resolution: Corrupted (game file missing: {gameLocFilePath})");
            return LocalizationState.Corrupted;
        }

        string actualSha;
        try
        {
            actualSha = await HashHelper.ComputeFileSha256Async(gameLocFilePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"State resolution: Corrupted (hash computation failed: {ex.Message})");
            return LocalizationState.Corrupted;
        }

        if (!string.Equals(actualSha, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error("State resolution: Corrupted (hash mismatch)");
            return LocalizationState.Corrupted;
        }

        // Step 3: File verified — determine release state
        if (current == null)
        {
            _logger.Debug("State resolution: WaitingForRelease (current=null)");
            return LocalizationState.WaitingForRelease;
        }

        if (string.IsNullOrEmpty(current.PublicId))
        {
            _logger.Warning("State resolution: WaitingForRelease (current.PublicId is null/empty)");
            return LocalizationState.WaitingForRelease;
        }

        // Primary identity: public_id only (ordinal exact)
        if (string.Equals(metadata.PublicId, current.PublicId, StringComparison.Ordinal))
        {
            _logger.Debug($"State resolution: UpToDate (public_id={metadata.PublicId})");
            return LocalizationState.UpToDate;
        }

        _logger.Debug($"State resolution: UpdateAvailable (installed={metadata.PublicId}, current={current.PublicId})");
        return LocalizationState.UpdateAvailable;
    }
}
