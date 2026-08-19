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
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = _sessionStore.GetSessionDir(sessionId);
        _logger.Info($"Update staging started: {candidate.TagName} (session={sessionId})");

        try
        {
            Directory.CreateDirectory(sessionDir);

            // 1. Download manifest
            progress?.Report(new UpdateStageProgress("Отримання метаданих оновлення...", 0));
            var manifestAsset = FindManifestAsset(candidate);
            if (manifestAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "Manifest asset not found");

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
            var zipAsset = FindZipAsset(candidate, manifest.AssetName!);
            if (zipAsset == null)
                return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "ZIP asset not found");

            if (zipAsset.Size <= 0 || zipAsset.Size > GitHubUpdateClient.ZipMaxBytes)
                return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid ZIP size: {zipAsset.Size}");

            // 4. Optional GitHub digest cross-check
            if (!string.IsNullOrEmpty(zipAsset.Digest))
            {
                if (zipAsset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    var gitHubHash = zipAsset.Digest["sha256:".Length..].ToLowerInvariant();
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

            // 5. Download ZIP
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

            // 6. SHA-256 verification
            progress?.Report(new UpdateStageProgress("Перевірка цілісності...", 100));
            _logger.Debug("Update: verifying ZIP SHA-256");
            var actualSha = await HashHelper.ComputeFileSha256Async(zipPath, cancellationToken);

            if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
            {
                _logger.Error($"ZIP SHA mismatch: actual={actualSha} expected={expectedSha}");
                SafeDelete(zipPath);
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP SHA-256 mismatch");
            }
            _logger.Debug($"ZIP SHA-256 verified: {actualSha}");

            // 7. Validate ZIP structure
            progress?.Report(new UpdateStageProgress("Перевірка пакета...", 100));
            _logger.Debug("Update: validating ZIP structure");
            var extractedExePath = Path.Combine(sessionDir, ExeFileName);
            long extractedSize;

            try
            {
                extractedSize = ExtractSingleExeFromZip(zipPath, extractedExePath, cancellationToken);
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
            var expectedFileVersion = $"{candidate.Version}.0";
            var expectedProductVersion = candidate.Version.ToString();

            if (!string.Equals(fileVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal))
            {
                _logger.Error($"EXE FileVersion '{fileVersionInfo.FileVersion}' != expected '{expectedFileVersion}'");
                SafeDelete(extractedExePath);
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, $"FileVersion mismatch: {fileVersionInfo.FileVersion}");
            }

            if (!string.Equals(fileVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
            {
                _logger.Error($"EXE ProductVersion '{fileVersionInfo.ProductVersion}' != expected '{expectedProductVersion}'");
                SafeDelete(extractedExePath);
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, $"ProductVersion mismatch: {fileVersionInfo.ProductVersion}");
            }

            _logger.Debug($"EXE version verified: FileVersion={fileVersionInfo.FileVersion} ProductVersion={fileVersionInfo.ProductVersion}");

            // 9. Calculate staged EXE SHA-256
            var exeSha = await HashHelper.ComputeFileSha256Async(extractedExePath, cancellationToken);
            _logger.Debug($"Staged EXE SHA-256: {exeSha}");

            // 10. Write session
            progress?.Report(new UpdateStageProgress("Підготовка оновлення...", 100));
            var targetPath = Environment.ProcessPath ?? "";

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
            {
                _logger.Error("Session write failed");
                _sessionStore.CleanupSession(sessionId);
                return writeResult;
            }

            _logger.Info($"Update staging complete: {candidate.TagName} (session={sessionId})");
            return UpdatePackageResult.Success(session);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Update staging cancelled (session={sessionId})");
            _sessionStore.CleanupSession(sessionId);
            return UpdatePackageResult.Failure(UpdatePackageError.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error($"Update staging failed: {ex.Message}");
            _sessionStore.CleanupSession(sessionId);
            return UpdatePackageResult.Failure(UpdatePackageError.IoError, ex.Message);
        }
    }

    private GitHubReleaseAsset? FindManifestAsset(UpdateCandidate candidate)
    {
        return candidate.Release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, ManifestFileName, StringComparison.Ordinal) &&
            string.Equals(a.State, "uploaded", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(a.BrowserDownloadUrl));
    }

    private GitHubReleaseAsset? FindZipAsset(UpdateCandidate candidate, string assetName)
    {
        return candidate.Release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.Ordinal) &&
            string.Equals(a.State, "uploaded", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(a.BrowserDownloadUrl) &&
            a.Size > 0);
    }

    private static long ExtractSingleExeFromZip(string zipPath, string destinationPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count != 1)
            throw new InvalidOperationException($"ZIP must contain exactly 1 entry, found {archive.Entries.Count}");

        var entry = archive.Entries[0];

        if (!string.Equals(entry.FullName, ExeFileName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Entry name '{entry.FullName}' != '{ExeFileName}'");

        if (entry.CompressedLength == 0)
            throw new InvalidOperationException("ZIP entry is empty");

        long totalRead = 0;
        var buffer = new byte[81920];

        using var entryStream = entry.Open();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        int bytesRead;
        while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalRead += bytesRead;

            if (totalRead > GitHubUpdateClient.ExeMaxBytes)
                throw new InvalidOperationException($"Extracted EXE exceeded max size ({GitHubUpdateClient.ExeMaxBytes} bytes)");

            fileStream.Write(buffer, 0, bytesRead);
        }

        fileStream.Flush();
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
