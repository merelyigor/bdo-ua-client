using System.Text;
using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Tests.Services;

public class RestoreBackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();

    public RestoreBackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
    }

    public void Dispose()
    {
        ClearReadOnlyAttributes(_tempDir);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static void ClearReadOnlyAttributes(string dir)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
        }
        catch { }
    }

    private string CreateGameRoot(byte[]? content = null)
    {
        var gameDir = Path.Combine(_tempDir, "game", "ads");
        Directory.CreateDirectory(gameDir);
        var path = Path.Combine(gameDir, "languagedata_en.loc");
        File.WriteAllText(path, content != null ? Encoding.UTF8.GetString(content) : "game content");
        return Path.Combine(_tempDir, "game");
    }

    private string GameLocFilePath => Path.Combine(_tempDir, "game", "ads", "languagedata_en.loc");

    private RestoreBackupService CreateService(string? gameRoot = null)
    {
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);
        return new RestoreBackupService(backupStore, stateStore, _logger,
            gameRoot ?? Path.Combine(_tempDir, "game"));
    }

    private async Task<(string rpDir, BackupStore store)> CreateRestorePointAsync(
        byte[]? gameContent = null, int? gamePatch = 100, string? label = "test",
        byte[]? stateBytes = null, bool stateWasPresent = false)
    {
        var store = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot(gameContent ?? Encoding.UTF8.GetBytes("restore content"));
        var gameLocPath = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        var (rpDir, result) = await store.CreateRestorePointAsync(
            gameLocPath, gamePatch, label, stateBytes, stateWasPresent);

        Assert.True(result.IsSuccess, $"CreateRestorePointAsync failed: {result.Error}");
        Assert.NotNull(rpDir);
        return (rpDir!, store);
    }

    private static void RemoveInstallationStateMarker(string restorePointDir)
    {
        var metadataPath = Path.Combine(restorePointDir, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        metadata.InstallationState = null;
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, options));
    }

    // --- Catalog tests ---

    [Fact]
    public async Task Catalog_ValidPoints_ListedNewestFirst()
    {
        var store = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot();
        var gameLocPath = GameLocFilePath;

        await store.CreateRestorePointAsync(gameLocPath, 100, "first");
        await Task.Delay(50);
        await store.CreateRestorePointAsync(gameLocPath, 101, "second");
        await Task.Delay(50);
        await store.CreateRestorePointAsync(gameLocPath, 102, "third");

        var catalog = await store.ListRestorePointsAsync();

        Assert.Equal(3, catalog.Count);
        Assert.True(string.Compare(catalog[0].Id, catalog[1].Id, StringComparison.OrdinalIgnoreCase) > 0);
        Assert.True(string.Compare(catalog[1].Id, catalog[2].Id, StringComparison.OrdinalIgnoreCase) > 0);
    }

    [Fact]
    public async Task Catalog_CorruptMetadata_Skipped()
    {
        var (rpDir, store) = await CreateRestorePointAsync();
        var metadataPath = Path.Combine(rpDir, "metadata.json");
        File.WriteAllText(metadataPath, "{ invalid json !!!");

        var catalog = await store.ListRestorePointsAsync();

        Assert.Empty(catalog);
    }

    [Fact]
    public async Task Catalog_HashMismatch_Skipped()
    {
        var (rpDir, store) = await CreateRestorePointAsync();
        var snapshotPath = Path.Combine(rpDir, "languagedata_en.loc");
        File.WriteAllText(snapshotPath, "tampered content");

        var catalog = await store.ListRestorePointsAsync();

        Assert.Empty(catalog);
    }

    [Fact]
    public async Task Catalog_PathTraversal_Rejected()
    {
        var store = new BackupStore(_paths, _logger);

        var (_, _, err1) = await store.ResolveRestorePointAsync("..");
        var (_, _, err2) = await store.ResolveRestorePointAsync("/");
        var (_, _, err3) = await store.ResolveRestorePointAsync(Path.Combine("C:", "Windows"));

        Assert.Equal(RestoreError.RestorePointNotFound, err1);
        Assert.Equal(RestoreError.RestorePointNotFound, err2);
        Assert.Equal(RestoreError.RestorePointNotFound, err3);
    }

    [Fact]
    public async Task Catalog_LegacyPointWithoutMarker_HasStateFile_IsRestorable()
    {
        var store = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot();
        var gameLocPath = GameLocFilePath;

        var (rpDir, _) = await store.CreateRestorePointAsync(gameLocPath, 100, "legacy");
        Assert.NotNull(rpDir);

        var stateFilePath = Path.Combine(rpDir!, "installation-state.json");
        File.WriteAllText(stateFilePath, "{\"legacy\":true}");

        RemoveInstallationStateMarker(rpDir!);

        var metadataPath = Path.Combine(rpDir!, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Null(metadata.InstallationState);

        var catalog = await store.ListRestorePointsAsync();
        Assert.Single(catalog);
        Assert.True(catalog[0].IsRestorable);
    }

    [Fact]
    public async Task Catalog_LegacyPointWithoutMarker_NoStateFile_NotRestorable()
    {
        var store = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot();
        var gameLocPath = GameLocFilePath;

        var (rpDir, _) = await store.CreateRestorePointAsync(gameLocPath, 100, "legacy_no_state");
        Assert.NotNull(rpDir);

        RemoveInstallationStateMarker(rpDir!);

        var metadataPath = Path.Combine(rpDir!, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Null(metadata.InstallationState);

        var catalog = await store.ListRestorePointsAsync();
        Assert.Single(catalog);
        Assert.False(catalog[0].IsRestorable);
    }

    // --- Create tests ---

    [Fact]
    public async Task Create_StatePresent_SnapshotSaved()
    {
        var stateBytes = Encoding.UTF8.GetBytes("{\"mode_slug\":\"full\",\"public_id\":\"01ABC\",\"version\":1}");
        var (rpDir, _) = await CreateRestorePointAsync(stateBytes: stateBytes, stateWasPresent: true);

        var stateFilePath = Path.Combine(rpDir, "installation-state.json");
        Assert.True(File.Exists(stateFilePath));
        Assert.Equal(stateBytes, File.ReadAllBytes(stateFilePath));

        var metadataPath = Path.Combine(rpDir, "metadata.json");
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("present", metadata.InstallationState);
    }

    [Fact]
    public async Task Create_StateAbsent_MarkerSaved()
    {
        var (rpDir, _) = await CreateRestorePointAsync(stateWasPresent: false);

        var stateFilePath = Path.Combine(rpDir, "installation-state.json");
        Assert.False(File.Exists(stateFilePath));

        var metadataPath = Path.Combine(rpDir, "metadata.json");
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("absent", metadata.InstallationState);
    }

    // --- Restore tests ---

    [Fact]
    public async Task Restore_Success_GameFileRestored()
    {
        var originalContent = Encoding.UTF8.GetBytes("original game content");
        var (rpDir, _) = await CreateRestorePointAsync(gameContent: originalContent);

        File.WriteAllText(GameLocFilePath, "modified game content");
        Assert.NotEqual(originalContent, File.ReadAllBytes(GameLocFilePath));

        var service = CreateService();
        var result = await service.RestoreAsync(Path.GetFileName(rpDir));

        Assert.True(result.IsSuccess);
        Assert.Equal(originalContent, File.ReadAllBytes(GameLocFilePath));
    }

    [Fact]
    public async Task Restore_Success_StatePresent_Restored()
    {
        var stateBytes = Encoding.UTF8.GetBytes("{\"mode_slug\":\"full\",\"public_id\":\"01ABC\",\"version\":1}");
        var (rpDir, _) = await CreateRestorePointAsync(stateBytes: stateBytes, stateWasPresent: true);

        var service = CreateService();
        var result = await service.RestoreAsync(Path.GetFileName(rpDir));

        Assert.True(result.IsSuccess);

        var installedPath = Path.Combine(_paths.StateDir, "installation.json");
        Assert.True(File.Exists(installedPath));
        Assert.Equal(stateBytes, File.ReadAllBytes(installedPath));
    }

    [Fact]
    public async Task Restore_Success_StateAbsent_RemovesCurrentState()
    {
        var (rpDir, _) = await CreateRestorePointAsync(stateWasPresent: false);

        var installedPath = Path.Combine(_paths.StateDir, "installation.json");
        File.WriteAllText(installedPath, "{\"to\":\"be deleted\"}");
        Assert.True(File.Exists(installedPath));

        var service = CreateService();
        var result = await service.RestoreAsync(Path.GetFileName(rpDir));

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(installedPath));
    }

    [Fact]
    public async Task Restore_CorruptPoint_ZeroMutation()
    {
        var store = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot();
        var gameLocPath = GameLocFilePath;

        var (rpDir, _) = await store.CreateRestorePointAsync(gameLocPath, 100, "corrupt");
        Assert.NotNull(rpDir);

        File.Delete(Path.Combine(rpDir!, "languagedata_en.loc"));

        var gameBefore = File.ReadAllBytes(GameLocFilePath);
        var installedPath = Path.Combine(_paths.StateDir, "installation.json");
        File.WriteAllText(installedPath, "{\"existing\":\"state\"}");
        var stateBefore = File.ReadAllText(installedPath);

        var service = CreateService();
        var result = await service.RestoreAsync(Path.GetFileName(rpDir!));

        Assert.False(result.IsSuccess);
        Assert.Equal(gameBefore, File.ReadAllBytes(GameLocFilePath));
        Assert.Equal(stateBefore, File.ReadAllText(installedPath));
    }

    [Fact]
    public async Task Restore_StateRestoreFailure_RollsBack()
    {
        var originalContent = Encoding.UTF8.GetBytes("original game content");
        var stateBytes = Encoding.UTF8.GetBytes("{\"mode_slug\":\"full\",\"public_id\":\"01ABC\",\"version\":1}");
        var (rpDir, _) = await CreateRestorePointAsync(
            gameContent: originalContent, stateBytes: stateBytes, stateWasPresent: true);

        File.WriteAllText(GameLocFilePath, "modified game content");

        var installedPath = Path.Combine(_paths.StateDir, "installation.json");
        File.WriteAllText(installedPath, "{\"old\":\"state\"}");
        File.SetAttributes(installedPath, FileAttributes.ReadOnly);

        var service = CreateService();
        var result = await service.RestoreAsync(Path.GetFileName(rpDir));

        File.SetAttributes(installedPath, FileAttributes.Normal);

        Assert.False(result.IsSuccess);
        Assert.Equal(Encoding.UTF8.GetBytes("modified game content"), File.ReadAllBytes(GameLocFilePath));
    }

    [Fact]
    public async Task Restore_PointNotFound_ReturnsError()
    {
        CreateGameRoot();
        var service = CreateService();
        var result = await service.RestoreAsync("nonexistent_point_000");

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.RestorePointNotFound, result.Error);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
