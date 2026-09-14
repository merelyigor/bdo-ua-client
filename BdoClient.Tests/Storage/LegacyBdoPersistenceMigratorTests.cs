using BdoClient.Logging;
using BdoClient.Storage;

namespace BdoClient.Tests.Storage;

public sealed class LegacyBdoPersistenceMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "BdoClientMigrationTests_" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _legacyPaths;
    private readonly GamePersistencePaths _scopedPaths;

    public LegacyBdoPersistenceMigratorTests()
    {
        _legacyPaths = new AppPaths(_root);
        _scopedPaths = _legacyPaths.GetGamePersistencePaths("black-desert-online");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void MigrateIfNeeded_MovesLegacyConfigStateAndBackupsToBdoScope()
    {
        Directory.CreateDirectory(_legacyPaths.StateDir);
        Directory.CreateDirectory(_legacyPaths.OriginalBackupDir);
        Directory.CreateDirectory(_legacyPaths.RestorePointsDir);
        File.WriteAllText(_legacyPaths.ConfigFile,
            "{\"game_path\":\"C:\\\\Black Desert\",\"last_mode\":\"full-ukrainian\",\"autostart_prompt_dismissed\":true}");
        File.WriteAllText(_legacyPaths.InstallationFile, "legacy-state");
        File.WriteAllText(Path.Combine(_legacyPaths.OriginalBackupDir, "languagedata_en.loc"), "original");
        var restorePoint = Path.Combine(_legacyPaths.RestorePointsDir, "legacy-point");
        Directory.CreateDirectory(restorePoint);
        File.WriteAllText(Path.Combine(restorePoint, "languagedata_en.loc"), "restore-point");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();

        var migratedConfig = new ConfigStore(_scopedPaths, new NullLogger()).Load();
        Assert.Equal(FileLoadStatus.Valid, migratedConfig.Status);
        Assert.Equal("C:\\Black Desert", migratedConfig.Value!.GamePath);
        Assert.Equal("full-ukrainian", migratedConfig.Value.LastMode);
        var migratedApplicationConfig = new ApplicationConfigStore(_legacyPaths, new NullLogger()).Load();
        Assert.Equal(FileLoadStatus.Valid, migratedApplicationConfig.Status);
        Assert.True(migratedApplicationConfig.Value!.AutostartPromptDismissed);
        Assert.Equal("legacy-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_scopedPaths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.Equal("restore-point", File.ReadAllText(Path.Combine(
            _scopedPaths.RestorePointsDir, "legacy-point", "languagedata_en.loc")));
        Assert.False(File.Exists(_legacyPaths.ConfigFile));
        Assert.False(Directory.Exists(_legacyPaths.StateDir));
        Assert.False(Directory.Exists(_legacyPaths.BackupsDir));
    }

    [Fact]
    public void MigrateIfNeeded_IsIdempotentAfterSuccessfulMove()
    {
        Directory.CreateDirectory(_legacyPaths.StateDir);
        File.WriteAllText(_legacyPaths.InstallationFile, "legacy-state");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();
        var migratedBytes = File.ReadAllBytes(_scopedPaths.InstallationFile);

        migrator.MigrateIfNeeded();

        Assert.Equal(migratedBytes, File.ReadAllBytes(_scopedPaths.InstallationFile));
        Assert.False(Directory.Exists(_legacyPaths.StateDir));
    }

    [Fact]
    public async Task MigrateIfNeeded_ContinuesRemainingComponentsWhenConfigWasAlreadyMigrated()
    {
        Directory.CreateDirectory(_legacyPaths.StateDir);
        Directory.CreateDirectory(_legacyPaths.OriginalBackupDir);
        File.WriteAllText(_legacyPaths.ConfigFile,
            "{\"game_path\":\"C:\\\\Black Desert\",\"last_mode\":\"full-ukrainian\",\"autostart_prompt_dismissed\":true}");
        File.WriteAllText(_legacyPaths.InstallationFile, "legacy-state");
        File.WriteAllText(Path.Combine(_legacyPaths.OriginalBackupDir, "languagedata_en.loc"), "original");

        Directory.CreateDirectory(_scopedPaths.Root);
        await new ConfigStore(_scopedPaths, new NullLogger())
            .SaveAsync(new Config { GamePath = "C:\\Canonical", LastMode = "canonical-mode" });
        await new ApplicationConfigStore(_legacyPaths, new NullLogger())
            .SaveAsync(new ApplicationConfig { AutostartPromptDismissed = false });

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();

        Assert.Equal("C:\\Canonical", new ConfigStore(_scopedPaths, new NullLogger()).Load().Value!.GamePath);
        Assert.Equal("canonical-mode", new ConfigStore(_scopedPaths, new NullLogger()).Load().Value!.LastMode);
        Assert.Equal("legacy-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("original", File.ReadAllText(Path.Combine(
            _scopedPaths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.True(File.Exists(_legacyPaths.ConfigFile));

        migrator.MigrateIfNeeded();

        Assert.Equal("C:\\Canonical", new ConfigStore(_scopedPaths, new NullLogger()).Load().Value!.GamePath);
        Assert.Equal("canonical-mode", new ConfigStore(_scopedPaths, new NullLogger()).Load().Value!.LastMode);
        Assert.False(new ApplicationConfigStore(_legacyPaths, new NullLogger()).Load().Value!.AutostartPromptDismissed);
        Assert.True(File.Exists(_legacyPaths.ConfigFile));
    }

    [Fact]
    public void MigrateIfNeeded_ContinuesBackupsWhenStateDestinationAlreadyExists()
    {
        Directory.CreateDirectory(_legacyPaths.OriginalBackupDir);
        Directory.CreateDirectory(_scopedPaths.StateDir);
        File.WriteAllText(Path.Combine(_legacyPaths.OriginalBackupDir, "languagedata_en.loc"), "legacy-original");
        File.WriteAllText(_scopedPaths.InstallationFile, "canonical-state");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();

        Assert.Equal("canonical-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("legacy-original", File.ReadAllText(Path.Combine(
            _scopedPaths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.False(Directory.Exists(_legacyPaths.BackupsDir));
    }

    [Fact]
    public void MigrateIfNeeded_DoesNotOverwriteCanonicalStateWhenBothExist()
    {
        Directory.CreateDirectory(_legacyPaths.StateDir);
        Directory.CreateDirectory(_scopedPaths.StateDir);
        File.WriteAllText(_legacyPaths.InstallationFile, "legacy-state");
        File.WriteAllText(_scopedPaths.InstallationFile, "canonical-state");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();

        Assert.Equal("canonical-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("legacy-state", File.ReadAllText(_legacyPaths.InstallationFile));
    }

    [Fact]
    public void MigrateIfNeeded_PreservesMalformedLegacyBytesForFailClosedRead()
    {
        Directory.CreateDirectory(_legacyPaths.StateDir);
        File.WriteAllText(_legacyPaths.InstallationFile, "{ malformed");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());
        migrator.MigrateIfNeeded();
        var store = new InstallationStateStore(_scopedPaths, new NullLogger());

        var result = store.Load();

        Assert.Equal(FileLoadStatus.Invalid, result.Status);
        Assert.Equal("{ malformed", File.ReadAllText(_scopedPaths.InstallationFile));
    }

    [Fact]
    public void MigrateIfNeeded_PreservesMalformedLegacyConfigAndMigratesIndependentState()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_legacyPaths.StateDir);
        Directory.CreateDirectory(_legacyPaths.OriginalBackupDir);
        Directory.CreateDirectory(_legacyPaths.RestorePointsDir);
        const string malformedConfig = "{ malformed";
        File.WriteAllText(_legacyPaths.ConfigFile, malformedConfig);
        File.WriteAllText(_legacyPaths.InstallationFile, "valid-state");
        File.WriteAllText(Path.Combine(_legacyPaths.OriginalBackupDir, "languagedata_en.loc"), "valid-original");
        var restorePoint = Path.Combine(_legacyPaths.RestorePointsDir, "valid-point");
        Directory.CreateDirectory(restorePoint);
        File.WriteAllText(Path.Combine(restorePoint, "languagedata_en.loc"), "valid-restore-point");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());

        migrator.MigrateIfNeeded();

        Assert.Equal(malformedConfig, File.ReadAllText(_legacyPaths.ConfigFile));
        Assert.False(File.Exists(_scopedPaths.ConfigFile));
        Assert.False(File.Exists(_legacyPaths.ApplicationConfigFile));
        Assert.Equal("valid-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("valid-original", File.ReadAllText(Path.Combine(
            _scopedPaths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.Equal("valid-restore-point", File.ReadAllText(Path.Combine(
            _scopedPaths.RestorePointsDir, "valid-point", "languagedata_en.loc")));
        Assert.False(Directory.Exists(_legacyPaths.StateDir));
        Assert.False(Directory.Exists(_legacyPaths.BackupsDir));

        migrator.MigrateIfNeeded();

        Assert.Equal(malformedConfig, File.ReadAllText(_legacyPaths.ConfigFile));
        Assert.Equal("valid-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.False(File.Exists(_scopedPaths.ConfigFile));
        Assert.False(File.Exists(_legacyPaths.ApplicationConfigFile));
    }

    [Fact]
    public void MigrateIfNeeded_PreservesNullLegacyConfigAndMigratesIndependentState()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_legacyPaths.StateDir);
        Directory.CreateDirectory(_legacyPaths.OriginalBackupDir);
        Directory.CreateDirectory(_legacyPaths.RestorePointsDir);
        const string nullConfig = "null";
        File.WriteAllText(_legacyPaths.ConfigFile, nullConfig);
        File.WriteAllText(_legacyPaths.InstallationFile, "valid-state");
        File.WriteAllText(Path.Combine(_legacyPaths.OriginalBackupDir, "languagedata_en.loc"), "valid-original");
        var restorePoint = Path.Combine(_legacyPaths.RestorePointsDir, "valid-point");
        Directory.CreateDirectory(restorePoint);
        File.WriteAllText(Path.Combine(restorePoint, "languagedata_en.loc"), "valid-restore-point");

        var migrator = new LegacyBdoPersistenceMigrator(_legacyPaths, _scopedPaths, new NullLogger());

        migrator.MigrateIfNeeded();

        Assert.Equal(nullConfig, File.ReadAllText(_legacyPaths.ConfigFile));
        Assert.False(File.Exists(_scopedPaths.ConfigFile));
        Assert.False(File.Exists(_legacyPaths.ApplicationConfigFile));
        Assert.Equal("valid-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.Equal("valid-original", File.ReadAllText(Path.Combine(
            _scopedPaths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.Equal("valid-restore-point", File.ReadAllText(Path.Combine(
            _scopedPaths.RestorePointsDir, "valid-point", "languagedata_en.loc")));
        Assert.False(Directory.Exists(_legacyPaths.StateDir));
        Assert.False(Directory.Exists(_legacyPaths.BackupsDir));

        migrator.MigrateIfNeeded();

        Assert.Equal(nullConfig, File.ReadAllText(_legacyPaths.ConfigFile));
        Assert.Equal("valid-state", File.ReadAllText(_scopedPaths.InstallationFile));
        Assert.False(File.Exists(_scopedPaths.ConfigFile));
        Assert.False(File.Exists(_legacyPaths.ApplicationConfigFile));
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
