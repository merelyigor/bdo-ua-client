namespace BdoClient.Update;

public readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
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

    public static AppVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        var parts = text.Split('.');
        if (parts.Length != 3) return null;

        if (!int.TryParse(parts[0], out var major) || major < 0) return null;
        if (!int.TryParse(parts[1], out var minor) || minor < 0) return null;
        if (!int.TryParse(parts[2], out var build) || build < 0) return null;

        return new AppVersion(major, minor, build);
    }

    public static AppVersion Parse(string text)
    {
        return TryParse(text) ?? throw new FormatException($"Invalid version format: '{text}'");
    }
}
