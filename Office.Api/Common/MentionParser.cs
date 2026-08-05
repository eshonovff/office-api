using System.Text.RegularExpressions;

namespace Office.Api.Common;

public static partial class MentionParser
{
    public static IReadOnlyList<string> ExtractUsernames(string text)
        => MentionPattern()
            .Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex(@"@([a-zA-Z0-9_.]+)")]
    private static partial Regex MentionPattern();
}
