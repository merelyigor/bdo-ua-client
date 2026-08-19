using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
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
        {
            _logger.Warning($"Update staging rejected: candidate {candidate.TagName} is a draft");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is a draft");
        }

        if (!candidate.Release.PublishedAt.HasValue)
        {
            _logger.Warning($"Update staging rejected: candidate {candidate.TagName} is not published");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is not published");
        }

        var expectedTag = $"v{candidate.Version}";
        if (!string.Equals(candidate.TagName, expectedTag, StringComparison.Ordinal))
        {
            _logger.Warning($"Update staging rejected: candidate tag '{candidate.TagName}' != expected '{expectedTag}'");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate tag does not match version");
        }

        var sessionId = Guid.NewGuid().ToString("D");
        var sessionDir = _sessionStore.GetSessionDir(sessionId);
        _logger.Info($"Update staging started: {candidate.TagName} (session={sessionId})");

        bool keepSession = false;
        try
        {
            Directory.CreateDirectory(sessionDir);

            // 1. Find and download manifest
            progress?.Report(new UpdateStageProgress("Отримання метаданих оновлення...", 0));
            var manifestAsset = FindExactlyOneAsset(candidate, ManifestFileName);
            if (manifestAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "Manifest asset not found or ambiguous");

            var manifestResult = await _gitHubClient.FetchManifestAsync(manifestAsset, cancellationToken);
            if (!manifestResult.IsSuccess)
                return UpdatePackageResult.Failure(UpdatePackageError.ManifestDownloadFailed, manifestResult.ErrorMessage!);

            var manifest = manifestResult.Value!;

            // 2. Validate manifest
            var validationResult = _manifestValidator.Validate(manifest, candidate);
            if (!validationResult.IsValid)
                return UpdatePackageResult.Failure(UpdatePackageError.ManifestInvalid, validationResult.ErrorMessage!);

            var expectedSha = validationResult.NormalizedSha256!;

            // 3. Find ZIP asset
            var zipAsset = FindExactlyOneAsset(candidate, manifest.AssetName!);
            if (zipAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "ZIP asset not found or ambiguous");

            if (zipAsset.Size <= 0 || zipAsset.Size > GitHubUpdateClient.ZipMaxBytes)
                return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid ZIP size: {zipAsset.Size}");

            // 4. GitHub digest cross-check
            if (!string.IsNullOrEmpty(zipAsset.Digest))
            {
                if (zipAsset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    var digestHex = zipAsset.Digest["sha256:".Length..];
                    if (digestHex.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(digestHex, "^[0-9a-fA-F]{64}$"))
                    {
                        _logger.Error($"Malformed GitHub digest: {zipAsset.Digest}");
                        return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "Malformed GitHub digest");
                    }
                    var gitHubHash = digestHex.ToLowerInvariant();
                    if (!string.Equals(gitHubHash, expectedSha, StringComparison.Ordinal))
                    {
                        _logger.Error($"GitHub digest {gitHubHash} != manifest SHA {expectedSha}");
                        return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "GitHub digest mismatch");
                    }
                    _logger.Debug("GitHub digest cross-check passed");
                }
                else
                {
                    _logger.Warning($"Unsupported GitHub digest format: {zipAsset.Digest}");
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "Unsupported GitHub digest format");
                }
            }

            // 5. Download ZIP with streamed SHA
            progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", 0));
            var zipPath = Path.Combine(sessionDir, "update-package.zip");

            var downloadProgress = new Progress<double>(pct =>
                progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", pct)));

            var downloadResult = await _gitHubClient.DownloadAssetAsync(
                zipAsset.BrowserDownloadUrl!, zipPath, zipAsset.Size, downloadProgress, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                SafeDelete(zipPath);
                return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, downloadResult.ErrorMessage!);
            }

            var downloadInfo = downloadResult.Value!;

            // 6. SHA-256 verification (streamed during download)
            progress?.Report(new UpdateStageProgress("Перевірка цілісності...", 100));
            _logger.Debug($"Update: verifying ZIP SHA-256 (streamed={downloadInfo.Sha256[..16]}...)");

            if (!string.Equals(downloadInfo.Sha256, expectedSha, StringComparison.Ordinal))
            {
                _logger.Error($"ZIP SHA mismatch: actual={downloadInfo.Sha256} expected={expectedSha}");
                SafeDelete(zipPath);
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP SHA-256 mismatch");
            }
            _logger.Debug($"ZIP SHA-256 verified: {downloadInfo.Sha256}");

            // 7. Validate and extract ZIP
            progress?.Report(new UpdateStageProgress("Перевірка пакета...", 100));
            _logger.Debug("Update: validating ZIP structure");
            var extractedExePath = Path.Combine(sessionDir, ExeFileName);
            long extractedSize;

            try
            {
                extractedSize = await ExtractSingleExeFromZipAsync(zipPath, extractedExePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"ZIP validation failed: {ex.Message}");
                SafeDelete(zipPath);
                SafeDelete(extractedExePath);
                return UpdatePackageResult.Failure(UpdatePackageError.PackageInvalid, ex.Message);
            }

            SafeDelete(zipPath);
            _logger.Debug($"Update: extracted {ExeFileName} ({extractedSize} bytes)");

            // 8. Validate EXE version metadata
            progress?.Report(new UpdateStageProgress("Перевірка оновлення...", 100));
            _logger.Debug("Update: validating EXE version metadata");
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(extractedExePath);

            if (!ExecutableVersionValidator.Validate(fileVersionInfo.FileVersion, fileVersionInfo.ProductVersion, candidate.Version, out var versionError))
            {
                _logger.Error($"EXE version invalid: {versionError}");
                SafeDelete(extractedExePath);
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, versionError!);
            }

            _logger.Debug($"EXE version verified: FileVersion={fileVersionInfo.FileVersion} ProductVersion={fileVersionInfo.ProductVersion}");

            // 9. Staged EXE SHA-256
            var exeSha = await HashHelper.ComputeFileSha256Async(extractedExePath, cancellationToken);
            _logger.Debug($"Staged EXE SHA-256: {exeSha}");

            // 10. Validate target path
            var targetPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathRooted(targetPath))
            {
                _logger.Error($"Invalid target path: '{targetPath}'");
                return UpdatePackageResult.Failure(UpdatePackageError.IoError, "Cannot determine current executable path");
            }

            if (!File.Exists(targetPath))
            {
                _logger.Error($"Target executable does not exist: '{targetPath}'");
                return UpdatePackageResult.Failure(UpdatePackageError.IoError, "Current executable file does not exist");
            }

            // 11. Write session
            progress?.Report(new UpdateStageProgress("Підготовка оновлення...", 100));
            var session = new UpdateSession
            {
                SchemaVersion = 1,
                SessionId = sessionId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = "staged",
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

    internal static GitHubReleaseAsset? FindExactlyOneAsset(UpdateCandidate candidate, string assetName)
    {
        var exactNameMatches = candidate.Release.Assets?
            .Where(a => string.Equals(a.Name, assetName, StringComparison.Ordinal))
            .ToList();

        if (exactNameMatches == null || exactNameMatches.Count == 0)
            return null;

        if (exactNameMatches.Count > 1)
            return null;

        var asset = exactNameMatches[0];

        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            return null;

        if (asset.Size <= 0)
            return null;

        if (string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            return null;

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return null;

        return asset;
    }

    private static async Task<long> ExtractSingleExeFromZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken)
    {
        const long maxExeBytes = GitHubUpdateClient.ExeMaxBytes;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count != 1)
            throw new InvalidOperationException($"ZIP must contain exactly 1 entry, found {archive.Entries.Count}");

        var entry = archive.Entries[0];

        if (!string.Equals(entry.FullName, ExeFileName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Entry name '{entry.FullName}' != '{ExeFileName}'");

        if (entry.CompressedLength == 0)
            throw new InvalidOperationException("ZIP entry is empty");

        if (entry.Length > maxExeBytes)
            throw new InvalidOperationException($"Declared entry size {entry.Length} exceeds max ({maxExeBytes})");

        long totalRead = 0;
        var buffer = new byte[81920];

        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        int bytesRead;
        while ((bytesRead = await entryStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > maxExeBytes)
                throw new InvalidOperationException($"Extracted EXE exceeded max size ({maxExeBytes} bytes)");

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (totalRead == 0)
            throw new InvalidOperationException("Extracted EXE is empty");

        if (entry.Length > 0 && totalRead != entry.Length)
            throw new InvalidOperationException($"Extracted {totalRead} bytes != declared {entry.Length}");

        return totalRead;
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
