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

    public async Task<LocalizationStateResult> ResolveAsync(
        CurrentRelease? current,
        string gameLocFilePath,
        string? selectedModeSlug = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Load installation state
        var loadResult = _stateStore.Load();

        if (loadResult.Status == FileLoadStatus.Missing)
        {
            _logger.Debug("State resolution: NotInstalled (metadata missing)");
            return LocalizationStateResult.Success(LocalizationState.NotInstalled);
        }

        if (loadResult.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning($"State resolution: InstalledVersionUnknown (metadata invalid: {loadResult.Error})");
            return LocalizationStateResult.Success(LocalizationState.InstalledVersionUnknown);
        }

        var metadata = loadResult.Value!;

        if (metadata.Source == "official")
        {
            _logger.Debug("State resolution: NotInstalled (source=official)");
            return LocalizationStateResult.Success(LocalizationState.NotInstalled);
        }

        // Step 2: Mode mismatch — installed mode differs from selected mode
        if (selectedModeSlug != null
            && !string.IsNullOrWhiteSpace(metadata.ModeSlug)
            && !string.Equals(metadata.ModeSlug, selectedModeSlug, StringComparison.Ordinal))
        {
            _logger.Debug($"State resolution: NotInstalled (mode mismatch: installed={metadata.ModeSlug}, selected={selectedModeSlug})");
            return LocalizationStateResult.Success(LocalizationState.NotInstalled);
        }

        // Step 3: API-installed — verify actual file
        if (!File.Exists(gameLocFilePath))
        {
            _logger.Error($"State resolution: Corrupted (game file missing: {gameLocFilePath})");
            return LocalizationStateResult.Success(LocalizationState.Corrupted);
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
            return LocalizationStateResult.Success(LocalizationState.Corrupted);
        }

        if (!string.Equals(actualSha, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error("State resolution: Corrupted (hash mismatch)");
            return LocalizationStateResult.Success(LocalizationState.Corrupted);
        }

        // Step 4: File verified — determine release state
        if (current == null)
        {
            _logger.Debug("State resolution: WaitingForRelease (current=null)");
            return LocalizationStateResult.Success(LocalizationState.WaitingForRelease);
        }

        if (string.IsNullOrWhiteSpace(current.PublicId))
        {
            _logger.Warning("State resolution: WaitingForRelease (current.PublicId is empty/null/whitespace — malformed server metadata)");
            return LocalizationStateResult.WithWarning(LocalizationState.WaitingForRelease,
                "Current release metadata is invalid: public_id is empty");
        }

        // Primary identity: public_id only (ordinal exact)
        if (string.Equals(metadata.PublicId, current.PublicId, StringComparison.Ordinal))
        {
            _logger.Debug($"State resolution: UpToDate (public_id={metadata.PublicId})");
            return LocalizationStateResult.Success(LocalizationState.UpToDate);
        }

        _logger.Debug($"State resolution: UpdateAvailable (installed={metadata.PublicId}, current={current.PublicId})");
        return LocalizationStateResult.Success(LocalizationState.UpdateAvailable);
    }
}
