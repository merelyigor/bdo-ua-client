using System.Diagnostics;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

public sealed class UpdatePackageService
{
    private const string ExeFileName = "BDO-UA-Client.exe";
    private const string ManifestFileName = "release-manifest.json";

    private readonly GitHubUpdateClient _gitHubClient;
    private readonly UpdateManifestValidator _manifestValidator;
    private readonly UpdateSessionStore _sessionStore;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    public UpdatePackageService(
        GitHubUpdateClient gitHubClient,
        UpdateManifestValidator manifestValidator,
        UpdateSessionStore sessionStore,
        AppPaths appPaths,
        ILogger logger)
    {
        _gitHubClient = gitHubClient;
        _manifestValidator = manifestValidator;
        _sessionStore = sessionStore;
        _appPaths = appPaths;
        _logger = logger;
    }

    public async Task<UpdatePackageResult> StageUpdateAsync(
        UpdateCandidate candidate,
        AppVersionInfo currentVersionInfo,
        IProgress<UpdateStageProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!currentVersionInfo.IsPublicRelease || !currentVersionInfo.PublicVersion.HasValue)
        {
            _logger.Warning("Update staging rejected: current version is not a public release");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Current version is not a public release");
        }

        if (candidate.Version <= currentVersionInfo.PublicVersion.Value)
        {
            _logger.Warning($"Update staging rejected: candidate {candidate.Version} <= current {currentVersionInfo.PublicVersion.Value}");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate version is not newer than current");
        }

        if (candidate.Release.Draft)
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is a draft");

        if (!candidate.Release.PublishedAt.HasValue)
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is not published");

        var expectedTag = $"v{candidate.Version}";
        if (!string.Equals(candidate.TagName, expectedTag, StringComparison.Ordinal))
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate tag does not match version");

        var sessionId = Guid.NewGuid().ToString("D");
        var sessionDir = _sessionStore.GetSessionDir(sessionId);
        _logger.Info($"Update staging started: {candidate.TagName} (session={sessionId})");

        var keepSession = false;
        try
        {
            Directory.CreateDirectory(sessionDir);

            progress?.Report(new UpdateStageProgress("Отримання метаданих оновлення...", 0));
            var manifestAsset = FindExactlyOneAsset(candidate, ManifestFileName);
            if (manifestAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "Manifest asset not found or ambiguous");

            var manifestResult = await _gitHubClient.FetchManifestAsync(manifestAsset, cancellationToken);
            if (!manifestResult.IsSuccess)
                return UpdatePackageResult.Failure(UpdatePackageError.ManifestDownloadFailed, manifestResult.ErrorMessage!);

            var manifest = manifestResult.Value!;
            var validationResult = _manifestValidator.Validate(manifest, candidate);
            if (!validationResult.IsValid)
                return UpdatePackageResult.Failure(UpdatePackageError.ManifestInvalid, validationResult.ErrorMessage!);

            var expectedSha = validationResult.NormalizedSha256!;
            var exeAsset = FindExactlyOneAsset(candidate, manifest.AssetName!);
            if (exeAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "EXE asset not found or ambiguous");

            if (exeAsset.Size <= 0 || exeAsset.Size > GitHubUpdateClient.ExeMaxBytes)
                return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid EXE size: {exeAsset.Size}");

            if (!ValidateGitHubDigest(exeAsset, expectedSha))
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "GitHub digest mismatch or unsupported format");

            progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", 0));
            var exePath = Path.Combine(sessionDir, ExeFileName);
            var downloadProgress = new Progress<double>(pct =>
                progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", pct)));

            var downloadResult = await _gitHubClient.DownloadAssetAsync(
                exeAsset.BrowserDownloadUrl!, exePath, exeAsset.Size, downloadProgress, cancellationToken);
            if (!downloadResult.IsSuccess)
                return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, downloadResult.ErrorMessage!);

            var downloadInfo = downloadResult.Value!;
            progress?.Report(new UpdateStageProgress("Перевірка цілісності...", 100));
            if (!string.Equals(downloadInfo.Sha256, expectedSha, StringComparison.Ordinal))
            {
                _logger.Error($"EXE SHA mismatch: actual={downloadInfo.Sha256} expected={expectedSha}");
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "EXE SHA-256 mismatch");
            }

            progress?.Report(new UpdateStageProgress("Перевірка оновлення...", 100));
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
            if (!ExecutableVersionValidator.Validate(fileVersionInfo.FileVersion, fileVersionInfo.ProductVersion, candidate.Version, out var versionError))
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, versionError!);

            var exeSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(exeSha, downloadInfo.Sha256, StringComparison.Ordinal))
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "Staged EXE SHA-256 mismatch");

            var targetPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathRooted(targetPath) || !File.Exists(targetPath))
                return UpdatePackageResult.Failure(UpdatePackageError.IoError, "Current executable file is not available");

            progress?.Report(new UpdateStageProgress("Підготовка оновлення...", 100));
            var session = new UpdateSession
            {
                SchemaVersion = 1,
                SessionId = sessionId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = UpdateSession.StateStaged,
                CurrentVersion = currentVersionInfo.PublicVersion?.ToString() ?? currentVersionInfo.RawVersion,
                TargetVersion = candidate.Version.ToString(),
                TargetTag = candidate.TagName,
                TargetPath = targetPath,
                ParentPid = Environment.ProcessId,
                PackageAssetName = manifest.AssetName!,
                PackageSha256 = expectedSha,
                StagedExeSha256 = exeSha
            };

            var writeResult = _sessionStore.WriteSession(session);
            if (!writeResult.IsSuccess)
                return writeResult;

            keepSession = true;
            _logger.Info($"Update staging complete: {candidate.TagName} (session={sessionId})");
            return UpdatePackageResult.Success(session);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Update staging cancelled (session={sessionId})");
            return UpdatePackageResult.Failure(UpdatePackageError.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error($"Update staging failed: {ex.Message}");
            return UpdatePackageResult.Failure(UpdatePackageError.IoError, ex.Message);
        }
        finally
        {
            if (!keepSession)
                _sessionStore.CleanupSession(sessionId);
        }
    }

    private bool ValidateGitHubDigest(GitHubReleaseAsset asset, string expectedSha)
    {
        if (string.IsNullOrEmpty(asset.Digest))
            return true;

        if (!asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning($"Unsupported GitHub digest format: {asset.Digest}");
            return false;
        }

        var digest = asset.Digest["sha256:".Length..];
        if (digest.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^[0-9a-fA-F]{64}$"))
        {
            _logger.Error($"Malformed GitHub digest: {asset.Digest}");
            return false;
        }

        return string.Equals(digest, expectedSha, StringComparison.OrdinalIgnoreCase);
    }

    internal static GitHubReleaseAsset? FindExactlyOneAsset(UpdateCandidate candidate, string assetName)
    {
        var matches = candidate.Release.Assets?
            .Where(a => string.Equals(a.Name, assetName, StringComparison.Ordinal))
            .ToList();

        if (matches == null || matches.Count != 1)
            return null;

        var asset = matches[0];
        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase) ||
            asset.Size <= 0 || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            return null;

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return null;

        return asset;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

public sealed class UpdateStageProgress
{
    public string Message { get; }
    public double Percent { get; }

    public UpdateStageProgress(string message, double percent)
    {
        Message = message;
        Percent = percent;
    }
}
