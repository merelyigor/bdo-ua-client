using BdoClient.Models;

namespace BdoClient.Services;

internal static class DynamicModePolicy
{
    public static List<LocalizationMode> GetInstallableModes(List<LocalizationMode>? allModes)
    {
        if (allModes == null) return new List<LocalizationMode>();

        return allModes.Where(m => IsStructurallyInstallable(m)).ToList();
    }

    public static bool IsStructurallyInstallable(LocalizationMode mode)
    {
        if (string.IsNullOrWhiteSpace(mode.Slug)) return false;
        if (mode.Current == null) return false;

        var c = mode.Current;
        if (string.IsNullOrWhiteSpace(c.PublicId)) return false;
        if (string.IsNullOrWhiteSpace(c.DownloadUrl)) return false;
        if (string.IsNullOrWhiteSpace(c.Sha256)) return false;
        if (c.SizeBytes <= 0) return false;
        if (c.Version <= 0) return false;
        if (c.Patch <= 0) return false;

        if (!Uri.TryCreate(c.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return true;
    }

    public static string GetDisplayName(LocalizationMode mode)
    {
        if (!string.IsNullOrWhiteSpace(mode.PublicName))
            return mode.PublicName;
        if (!string.IsNullOrWhiteSpace(mode.Slug))
            return mode.Slug;
        return "Невідомий режим";
    }

    public static string FormatReleaseLine(LocalizationMode mode)
    {
        var c = mode.Current;
        if (c == null) return "";

        var parts = new List<string>();
        if (c.Version > 0) parts.Add($"v{c.Version}");
        if (c.Patch > 0) parts.Add($"patch {c.Patch}");

        var dateStr = FormatPublishedDate(c.PublishedAt);
        if (dateStr != null) parts.Add($"реліз {dateStr}");

        return string.Join(" • ", parts);
    }

    public static string? FormatPublishedDate(string? publishedAt)
    {
        if (string.IsNullOrWhiteSpace(publishedAt)) return null;
        if (DateTimeOffset.TryParse(publishedAt, out var date))
            return date.ToLocalTime().ToString("dd.MM.yyyy");
        return null;
    }

    public static string? ResolveInitialSelection(
        string? savedLastMode, List<LocalizationMode> installableModes)
    {
        if (installableModes.Count == 0) return null;

        if (savedLastMode != null)
        {
            var match = installableModes.FirstOrDefault(
                m => string.Equals(m.Slug, savedLastMode, StringComparison.Ordinal));
            if (match != null) return match.Slug;
        }

        return installableModes[0].Slug;
    }
}
