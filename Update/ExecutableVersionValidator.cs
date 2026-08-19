namespace BdoClient.Update;

public static class ExecutableVersionValidator
{
    public static bool Validate(string? fileVersion, string? productVersion, AppVersion target, out string? error)
    {
        var expectedFileVersion = $"{target.Major}.{target.Minor}.{target.Build}.0";
        var expectedProductVersion = $"{target.Major}.{target.Minor}.{target.Build}";

        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            error = "FileVersion is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(productVersion))
        {
            error = "ProductVersion is empty";
            return false;
        }

        if (!string.Equals(fileVersion, expectedFileVersion, StringComparison.Ordinal))
        {
            error = $"FileVersion '{fileVersion}' != expected '{expectedFileVersion}'";
            return false;
        }

        if (!string.Equals(productVersion, expectedProductVersion, StringComparison.Ordinal))
        {
            error = $"ProductVersion '{productVersion}' != expected '{expectedProductVersion}'";
            return false;
        }

        error = null;
        return true;
    }
}
