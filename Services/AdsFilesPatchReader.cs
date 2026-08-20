using System.Text.RegularExpressions;

namespace BdoClient.Services;

public static class AdsFilesPatchReader
{
    private static readonly Regex EnglishEntry = new(
        @"^\s*languagedata_en\.loc\s+([1-9][0-9]*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int? TryReadPatch(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
            return null;

        try
        {
            var root = Path.GetFullPath(gameRoot);
            var path = Path.Combine(root, "ads_files");
            if (!File.Exists(path))
                return null;

            var patches = new List<int>();
            foreach (var line in File.ReadLines(path))
            {
                var match = EnglishEntry.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var patch) && patch > 0)
                    patches.Add(patch);
            }

            return patches.Count == 1 ? patches[0] : null;
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
