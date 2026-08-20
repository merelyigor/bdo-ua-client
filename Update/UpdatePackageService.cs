using System.Diagnostics;
using System.IO.Compression;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

public sealed class UpdatePackageService
{
    private const string ExeFileName = "BDO-UA-Client.exe";
    private const string ManifestFileName = "release-manifest.json";
    private const string PackageEntryName = "BDO-UA-Client.exe";
    public const long ZipMaxBytes = 100_000_000;

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
            var exePath = Path.Combine(sessionDir, ExeFileName);
            var downloadProgress = new Progress<double>(pct =>
                progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", pct)));

            string packageAssetName = ExeFileName;
            string packageSha;
            if (!string.IsNullOrWhiteSpace(manifest.PackageName) && !string.IsNullOrWhiteSpace(manifest.PackageSha256))
            {
                var packageAsset = FindExactlyOneAsset(candidate, manifest.PackageName);
                if (packageAsset == null)
                    return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "ZIP package not found or ambiguous");
                if (packageAsset.Size <= 0 || packageAsset.Size > ZipMaxBytes)
                    return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid ZIP size: {packageAsset.Size}");
                if (!ValidateGitHubDigest(packageAsset, manifest.PackageSha256))
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP GitHub digest mismatch or unsupported format");

                var packagePath = Path.Combine(sessionDir, manifest.PackageName);
                var packageResult = await _gitHubClient.DownloadAssetAsync(
                    packageAsset.BrowserDownloadUrl!, packagePath, packageAsset.Size, downloadProgress, cancellationToken);
                if (!packageResult.IsSuccess)
                    return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, packageResult.ErrorMessage!);
                packageSha = packageResult.Value!.Sha256;
                if (!string.Equals(packageSha, manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP SHA-256 mismatch");
                var extractionResult = await ExtractValidatedExeAsync(packagePath, exePath, expectedSha, candidate.Version, cancellationToken);
                if (!extractionResult.IsValid)
                    return UpdatePackageResult.Failure(UpdatePackageError.PackageInvalid, extractionResult.Error!);
            }
            else
            {
                var exeAsset = FindExactlyOneAsset(candidate, manifest.AssetName!);
                if (exeAsset == null)
                    return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "EXE asset not found or ambiguous");
                if (exeAsset.Size <= 0 || exeAsset.Size > GitHubUpdateClient.ExeMaxBytes)
                    return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid EXE size: {exeAsset.Size}");
                if (!ValidateGitHubDigest(exeAsset, expectedSha))
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "GitHub digest mismatch or unsupported format");
                var downloadResult = await _gitHubClient.DownloadAssetAsync(
                    exeAsset.BrowserDownloadUrl!, exePath, exeAsset.Size, downloadProgress, cancellationToken);
                if (!downloadResult.IsSuccess)
                    return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, downloadResult.ErrorMessage!);
                packageSha = downloadResult.Value!.Sha256;
            }

            progress?.Report(new UpdateStageProgress("Перевірка цілісності...", 100));
            var downloadedExeSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(downloadedExeSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error($"EXE SHA mismatch: actual={downloadedExeSha} expected={expectedSha}");
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "EXE SHA-256 mismatch");
            }

            progress?.Report(new UpdateStageProgress("Перевірка оновлення...", 100));
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
            if (!ExecutableVersionValidator.Validate(fileVersionInfo.FileVersion, fileVersionInfo.ProductVersion, candidate.Version, out var versionError))
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, versionError!);

            var exeSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(exeSha, expectedSha, StringComparison.OrdinalIgnoreCase))
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
                PackageAssetName = packageAssetName,
                PackageSha256 = packageSha,
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

    internal static async Task<(bool IsValid, string? Error)> ExtractValidatedExeAsync(
        string packagePath,
        string exePath,
        string expectedSha,
        AppVersion version,
        CancellationToken cancellationToken = default,
        Func<string?, string?, bool>? versionValidator = null)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count != 1)
                return (false, "ZIP must contain exactly one entry");
            var entry = archive.Entries[0];
            if (!string.Equals(entry.FullName, PackageEntryName, StringComparison.Ordinal) || entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                return (false, "ZIP entry must be exactly BDO-UA-Client.exe at archive root");
            if (entry.Length <= 0 || entry.Length > GitHubUpdateClient.ExeMaxBytes)
                return (false, "ZIP EXE entry size is invalid");
            using var input = entry.Open();
            await using (var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var stagedSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(stagedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                return (false, "Extracted EXE SHA-256 mismatch");
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (versionValidator == null)
            {
                if (!ExecutableVersionValidator.Validate(info.FileVersion, info.ProductVersion, version, out var error))
                    return (false, error);
            }
            else if (!versionValidator(info.FileVersion, info.ProductVersion))
            {
                return (false, "Executable version metadata mismatch");
            }
            return (true, null);
        }
        catch (InvalidDataException ex)
        {
            return (false, $"Malformed ZIP: {ex.Message}");
        }
        catch (IOException ex)
        {
            return (false, $"ZIP extraction failed: {ex.Message}");
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
