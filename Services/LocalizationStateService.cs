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
        CancellationToken cancellationToken = default,
        string? gameRoot = null)
    {
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

        var installedPatch = metadata.GamePatch is > 0 ? metadata.GamePatch : null;
        var localPatch = AdsFilesPatchReader.TryReadPatch(gameRoot ?? DeriveGameRoot(gameLocFilePath));

        if (metadata.Source == InstallationSource.Official)
        {
            _logger.Debug("State resolution: NotInstalled (source=official)");
            return LocalizationStateResult.Success(LocalizationState.NotInstalled);
        }

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
            if (installedPatch.HasValue && localPatch.HasValue && localPatch > installedPatch)
            {
                return ResolveAfterPatchTransition(current, metadata, installedPatch.Value, localPatch.Value, LocalizationPatchTransition.GameFileReplacedAfterPatch);
            }

            _logger.Warning($"State resolution: managed localization file changed (installed_public_id={metadata.PublicId})");
            return ResolveAfterManagedFileChanged(current, metadata.PublicId);
        }

        if (installedPatch.HasValue && localPatch.HasValue && localPatch > installedPatch)
        {
            return ResolveAfterPatchTransition(current, metadata, installedPatch.Value, localPatch.Value, LocalizationPatchTransition.ExistingLocalizationOutdated);
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
                "Актуальний українізатор ще не доступний. Перевірте оновлення пізніше.");
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

    private LocalizationStateResult ResolveAfterPatchTransition(
        CurrentRelease? current,
        InstallationMetadata metadata,
        int installedPatch,
        int localPatch,
        LocalizationPatchTransition transition)
    {
        var detail = transition == LocalizationPatchTransition.ExistingLocalizationOutdated
            ? $"Локалізація встановлена для патча {installedPatch}, а гра вже оновлена до патча {localPatch}."
            : $"Українська локалізація для патча {installedPatch} більше не активна. Гра оновлена до патча {localPatch}.";

        if (current == null)
            return LocalizationStateResult.WithPatchTransition(
                LocalizationState.WaitingForRelease, installedPatch, localPatch, transition,
                $"{detail} Актуальний українізатор для патча {localPatch} ще не доступний. Перевірте оновлення пізніше.");

        if (string.IsNullOrWhiteSpace(current.PublicId))
            return LocalizationStateResult.WithPatchTransition(
                LocalizationState.WaitingForRelease, installedPatch, localPatch, transition,
                $"{detail} Актуальний українізатор для патча {localPatch} ще не доступний. Перевірте оновлення пізніше.");

        return LocalizationStateResult.WithPatchTransition(
            LocalizationState.UpdateAvailable, installedPatch, localPatch, transition, detail);
    }

    private static LocalizationStateResult ResolveAfterManagedFileChanged(
        CurrentRelease? current,
        string? installedPublicId)
    {
        const string detail = "Встановлена локалізація більше не активна. Файл локалізації було змінено або замінено після встановлення.";

        if (current == null || string.IsNullOrWhiteSpace(current.PublicId))
            return LocalizationStateResult.WithManagedFileChanged(
                LocalizationState.WaitingForRelease,
                LocalizationPatchTransition.ManagedFileChanged,
                $"{detail} Актуальний українізатор ще не доступний. Перевірте оновлення пізніше.");

        return LocalizationStateResult.WithManagedFileChanged(
            LocalizationState.UpdateAvailable,
            LocalizationPatchTransition.ManagedFileChanged,
            detail);
    }

    private static string? DeriveGameRoot(string gameLocFilePath)
    {
        var adsDirectory = Path.GetDirectoryName(gameLocFilePath);
        return adsDirectory == null ? null : Directory.GetParent(adsDirectory)?.FullName;
    }
}
