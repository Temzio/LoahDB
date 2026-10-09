using System.Text.RegularExpressions;

namespace LoahDB;

internal static partial class LoahFullTextTokenizer
{
    public static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in TokenRegex().Matches(text.ToLowerInvariant()))
        {
            if (match.Value.Length > 0)
            {
                yield return match.Value;
            }
        }
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
