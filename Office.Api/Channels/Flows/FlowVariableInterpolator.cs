using System.Text.RegularExpressions;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Иваз кардани {{key}} дар матни паём. Дар лаҳзаи ФИРИСТОДАН амал мекунад (на дар лаҳзаи
/// захира), то тағйирёбандаҳо ҳамеша қиммати охирин дошта бошанд.
///
/// Пешвои калидҳо: contactFields (built-in — clientId, firstName, lastName, fullName,
/// username, chatLink, ниг. спека) ва variables (тағйирёбандаҳои корбарӣ, аз
/// action:set_variable/collect_input). variables болотар аз contactFields мераванд, агар
/// калиди якхела бошад — тағйирёбандаи корбарӣ хостаи қасдонаи муаллифи flow аст.
///
/// Калиди номаълум {{key}} БЕТАҒЙИР мемонад (на холӣ) — то хатои имлоӣ дар матни паём дар
/// dry-run/санҷиш возеҳ намоён шавад, на хомӯшона гум шавад.
/// </summary>
public static partial class FlowVariableInterpolator
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();

    public static string Interpolate(
        string text, IReadOnlyDictionary<string, string> variables, IReadOnlyDictionary<string, string> contactFields) =>
        TokenPattern().Replace(text, match =>
        {
            var key = match.Groups[1].Value;
            if (variables.TryGetValue(key, out var variableValue))
                return variableValue;
            return contactFields.TryGetValue(key, out var contactValue) ? contactValue : match.Value;
        });
}
