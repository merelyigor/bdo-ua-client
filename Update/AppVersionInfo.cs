using System.Reflection;

namespace BdoClient.Update;

public sealed class AppVersionInfo
{
    public string RawVersion { get; }
    public string DisplayVersion { get; }
    public bool IsPublicRelease { get; }
    public AppVersion? PublicVersion { get; }

    private AppVersionInfo(string rawVersion, string displayVersion, bool isPublicRelease, AppVersion? publicVersion)
    {
        RawVersion = rawVersion;
        DisplayVersion = displayVersion;
        IsPublicRelease = isPublicRelease;
        PublicVersion = publicVersion;
    }

    public static AppVersionInfo Detect()
    {
        var raw = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        return FromRawVersion(raw);
    }

    public static AppVersionInfo FromRawVersion(string raw)
    {
        if (raw == "unknown")
            return new AppVersionInfo(raw, "версія невідома", false, null);

        var publicVersion = AppVersion.TryParseCoreVersion(raw);
        if (publicVersion.HasValue)
        {
            var display = $"v{publicVersion.Value}";
            return new AppVersionInfo(raw, display, true, publicVersion);
        }

        var displayFallback = $"v{raw}";
        return new AppVersionInfo(raw, displayFallback, false, null);
    }
}
