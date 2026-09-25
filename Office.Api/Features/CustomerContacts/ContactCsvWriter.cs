using System.Text;

namespace Office.Api.Features.CustomerContacts;

/// <summary>
/// The contacts export for Excel. Separator ";" (Excel in Russian/Tajik settings expects it —
/// "," there is the decimal comma), UTF-8 with a BOM (without it Excel garbles Cyrillic), every
/// field quoted. Every cell a spreadsheet would read as a formula — starting with = + - @, a tab
/// or a line break — is led by a ' : a name like "=HYPERLINK(...)" that a stranger typed into
/// Instagram must stay text on the мизоҷ's computer (CSV injection).
/// </summary>
public static class ContactCsvWriter
{
    public const char Separator = ';';

    public record Row(
        string? Name,
        string? Username,
        string Channel,
        IReadOnlyList<string> Tags,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? LastMessageAt,
        IReadOnlyDictionary<string, string> Variables);

    public record Headers(string Name, string Username, string Profile, string Channel, string Tags, string FirstSeen, string LastMessage);

    public static readonly Headers Tajik = new("Ном", "Username", "Профил", "Канал", "Тегҳо", "Аввалин тамос", "Охирин паём");
    public static readonly Headers Russian = new("Имя", "Username", "Профиль", "Канал", "Теги", "Первый контакт", "Последнее сообщение");

    /// <param name="utcOffset">Dates are written in this local time (Dushanbe: +5).</param>
    public static byte[] Write(IReadOnlyList<Row> rows, Headers headers, TimeSpan utcOffset)
    {
        // One column per variable name, in a stable order — whatever the flows collected.
        var variableKeys = rows.SelectMany(r => r.Variables.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var csv = new StringBuilder();
        AppendLine(csv, [headers.Name, headers.Username, headers.Profile, headers.Channel, headers.Tags, headers.FirstSeen, headers.LastMessage, .. variableKeys]);

        foreach (var row in rows)
        {
            var profile = string.IsNullOrEmpty(row.Username) ? "" : $"https://instagram.com/{Uri.EscapeDataString(row.Username)}";
            AppendLine(csv,
            [
                row.Name ?? "",
                row.Username ?? "", // without "@" — that would be read as a formula and get a ' in front
                profile,
                row.Channel,
                string.Join(", ", row.Tags),
                Format(row.FirstSeenAt, utcOffset),
                row.LastMessageAt is null ? "" : Format(row.LastMessageAt.Value, utcOffset),
                .. variableKeys.Select(k => row.Variables.TryGetValue(k, out var v) ? v : ""),
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>One field: made harmless for a spreadsheet, then quoted (inner quotes doubled).</summary>
    public static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void AppendLine(StringBuilder csv, IEnumerable<string> fields)
    {
        csv.Append(string.Join(Separator, fields.Select(Field)));
        csv.Append("\r\n");
    }

    private static string Format(DateTimeOffset value, TimeSpan utcOffset) =>
        value.ToOffset(utcOffset).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
