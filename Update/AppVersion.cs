using System.Text.RegularExpressions;

namespace BdoClient.Update;

public readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private static readonly Regex CoreVersionRegex = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);

    private static readonly Regex ReleaseTagRegex = new(
        @"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Build { get; }

    public AppVersion(int major, int minor, int build)
    {
        Major = major;
        Minor = minor;
        Build = build;
    }

    public int CompareTo(AppVersion other)
    {
        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        return Build.CompareTo(other.Build);
    }

    public bool Equals(AppVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is AppVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Build);
    public override string ToString() => $"{Major}.{Minor}.{Build}";

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;
    public static bool operator ==(AppVersion left, AppVersion right) => left.Equals(right);
    public static bool operator !=(AppVersion left, AppVersion right) => !left.Equals(right);

    public static AppVersion? TryParseCoreVersion(string? text)
    {
        if (text == null) return null;

        var match = CoreVersionRegex.Match(text);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, out var major)) return null;
        if (!int.TryParse(match.Groups[2].Value, out var minor)) return null;
        if (!int.TryParse(match.Groups[3].Value, out var build)) return null;

        return new AppVersion(major, minor, build);
    }

    public static AppVersion? TryParseReleaseTag(string? text)
    {
        if (text == null) return null;

        var match = ReleaseTagRegex.Match(text);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, out var major)) return null;
        if (!int.TryParse(match.Groups[2].Value, out var minor)) return null;
        if (!int.TryParse(match.Groups[3].Value, out var build)) return null;

        return new AppVersion(major, minor, build);
    }
}
