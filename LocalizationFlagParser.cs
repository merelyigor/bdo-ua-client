using System.Text;

namespace BdoClient;

internal readonly record struct ParsedLocalizationName(
    string Title,
    IReadOnlyList<string> CountryCodes);

internal static class LocalizationFlagParser
{
    private const int RegionalIndicatorStart = 0x1F1E6;
    private const int RegionalIndicatorEnd = 0x1F1FF;

    public static ParsedLocalizationName Parse(string? value)
    {
        var text = value ?? string.Empty;
        var runes = text.EnumerateRunes().ToList();
        var codes = new List<string>();
        var utf16Offset = 0;

        while (runes.Count >= codes.Count + 2
            && IsRegionalIndicator(runes[codes.Count * 2])
            && IsRegionalIndicator(runes[codes.Count * 2 + 1]))
        {
            var first = runes[codes.Count * 2].Value - RegionalIndicatorStart;
            var second = runes[codes.Count * 2 + 1].Value - RegionalIndicatorStart;
            codes.Add($"{(char)('A' + first)}{(char)('A' + second)}");
            utf16Offset += runes[codes.Count * 2 - 2].Utf16SequenceLength;
            utf16Offset += runes[codes.Count * 2 - 1].Utf16SequenceLength;
        }

        if (codes.Count == 0)
            return new ParsedLocalizationName(text, codes);

        var title = text[utf16Offset..].TrimStart();
        return new ParsedLocalizationName(title, codes);
    }

    private static bool IsRegionalIndicator(Rune rune) =>
        rune.Value is >= RegionalIndicatorStart and <= RegionalIndicatorEnd;
}
