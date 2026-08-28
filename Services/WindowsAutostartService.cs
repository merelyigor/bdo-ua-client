using System;
using System.IO;
using Microsoft.Win32;
using BdoClient.Logging;

namespace BdoClient.Services;

/// <summary>
/// Per-user Windows autostart integration via the HKCU Run key.
/// The registry is the source of truth for whether autostart is enabled.
/// No elevation, service, scheduled task, or startup-folder shortcut is used.
/// </summary>
public sealed class WindowsAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BDO-UA-Client";
    private const string BackgroundArgument = "--background";

    private readonly string _executablePath;
    private readonly ILogger _logger;

    public WindowsAutostartService(string executablePath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path must not be null or empty.", nameof(executablePath));
        if (executablePath.Contains('"'))
            throw new ArgumentException("Executable path must not contain embedded quotes.", nameof(executablePath));
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Executable path must be fully qualified.", nameof(executablePath));

        _executablePath = executablePath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the canonical registry command: the executable path (always quoted)
    /// followed by the --background argument.
    /// </summary>
    public string BuildRunCommand()
    {
        return $"\"{_executablePath}\" {BackgroundArgument}";
    }

    /// <summary>
    /// Pure helper: returns true only when the registry value is exactly the canonical
    /// command for the given executable path plus --background. Windows paths are
    /// compared case-insensitively. No registry access is performed.
    /// </summary>
    public static bool MatchesCanonicalCommand(string? registryValue, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(registryValue))
            return false;
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            return false;

        var canonical = $"\"{executablePath}\" {BackgroundArgument}";
        return string.Equals(registryValue, canonical, StringComparison.OrdinalIgnoreCase);
    }

    public void Enable()
    {
        var command = BuildRunCommand();
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("Failed to open or create the autostart registry key.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
        _logger.Info("Autostart enabled in registry.");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null)
            return;

        if (key.GetValue(ValueName) == null)
            return;

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        _logger.Info("Autostart disabled in registry.");
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        if (key == null)
            return false;

        var value = key.GetValue(ValueName) as string;
        return MatchesCanonicalCommand(value, _executablePath);
    }
}
