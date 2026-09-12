using System.Text.RegularExpressions;

namespace BdoClient.Services;

public sealed class AdsFilesPatchReader
{
    private static readonly Regex EntryLine = new(
        @"^\s*(\S+)(?:\s+(.*?))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _adsFilesName;
    private readonly string _localizationFileName;

    internal AdsFilesPatchReader(string adsFilesName, string localizationFileName)
    {
        _adsFilesName = adsFilesName;
        _localizationFileName = localizationFileName;
    }

    // Compatibility entry point for existing BDO-focused callers and tests.
    public static int? TryReadPatch(string? gameRoot)
        => BdoGameDefinition.Default.TryReadInstalledPatch(gameRoot);

    internal int? ReadPatch(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
            return null;

        try
        {
            var root = Path.GetFullPath(gameRoot);
            var path = Path.Combine(root, _adsFilesName);
            if (!File.Exists(path))
                return null;

            var entryCount = 0;
            int? patch = null;
            foreach (var line in File.ReadLines(path))
            {
                var match = EntryLine.Match(line);
                if (!match.Success || !string.Equals(match.Groups[1].Value, _localizationFileName, StringComparison.Ordinal))
                    continue;

                entryCount++;
                var value = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
                if (entryCount == 1 && int.TryParse(value, out var parsedPatch) && parsedPatch > 0)
                    patch = parsedPatch;
            }

            return entryCount == 1 ? patch : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
