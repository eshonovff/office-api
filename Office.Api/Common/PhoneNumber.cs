using System.Text.RegularExpressions;

namespace Office.Api.Common;

public static partial class PhoneNumber
{
    /// <summary>
    /// Ба формати ягона "992XXXXXXXXX" (12 рақам) меорад. Қабул мекунад:
    /// "+992XXXXXXXXX", "992XXXXXXXXX" ё "XXXXXXXXX" (9 рақами маҳаллӣ).
    /// Агар формат нодуруст бошад, null бармегардонад.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digits = DigitsOnlyRegex().Replace(input, "");

        return digits.Length switch
        {
            9 => "992" + digits,
            12 when digits.StartsWith("992", StringComparison.Ordinal) => digits,
            _ => null,
        };
    }

    /// <summary>Барои параметри "phone_number"-и OsonSMS — префикси "992"-ро мебарорад.</summary>
    public static string ToLocalDigits(string normalized) => normalized["992".Length..];

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnlyRegex();
}
