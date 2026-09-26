using System.Text.RegularExpressions;
using Office.Api.Common;

namespace Office.Api.Features.CustomerContacts;

/// <summary>
/// The contacts export as an Excel file (.xlsx — see <see cref="XlsxSheet"/> for why not CSV).
/// Columns, left to right: №, name, Instagram (@username, a link to the profile), the details the
/// flows collected (the most filled first), tags, first contact, last message — and the account,
/// only when the contacts come from more than one. Names and details are what strangers typed into
/// Instagram: they are written as text, never as formulas.
/// </summary>
public static partial class ContactXlsxWriter
{
    public record Row(
        string? Name,
        string? Username,
        string Channel,
        IReadOnlyList<string> Tags,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? LastMessageAt,
        IReadOnlyDictionary<string, string> Variables);

    /// <param name="Details">Headers for the details the ready-made flows collect; any other key is shown as the мизоҷ named it.</param>
    public record Labels(
        string Sheet, string Number, string Name, string Instagram, string Tags, string FirstSeen, string LastMessage, string Account,
        IReadOnlyDictionary<string, string> Details);

    public static readonly Labels Tajik = new("Контактҳо", "№", "Ном", "Instagram", "Тегҳо", "Аввалин тамос", "Охирин паём", "Аккаунт",
        new Dictionary<string, string> { ["phone"] = "Телефон", ["name"] = "Ном (худаш навишт)", ["email"] = "Email" });
    public static readonly Labels Russian = new("Контакты", "№", "Имя", "Instagram", "Теги", "Первый контакт", "Последнее сообщение", "Аккаунт",
        new Dictionary<string, string> { ["phone"] = "Телефон", ["name"] = "Имя (написал сам)", ["email"] = "Email" });

    // Instagram's own rule for a username — anything else gets no link.
    [GeneratedRegex("^[A-Za-z0-9._]{1,30}$")]
    private static partial Regex InstagramUsername();

    /// <param name="utcOffset">Dates are shown in this local time (Dushanbe: +5).</param>
    public static byte[] Write(IReadOnlyList<Row> rows, Labels labels, TimeSpan utcOffset)
    {
        // One column per detail, the most filled first — the phone the flows asked everyone for
        // comes before a key only a few have.
        var variableKeys = rows.SelectMany(r => r.Variables.Keys)
            .GroupBy(k => k, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
        var showAccount = rows.Select(r => r.Channel).Distinct(StringComparer.Ordinal).Count() > 1;

        var cells = rows.Select((row, index) =>
        {
            var username = string.IsNullOrEmpty(row.Username) ? null : row.Username;
            List<XlsxCell> line =
            [
                XlsxCell.Count(index + 1),
                XlsxCell.Of(row.Name),
                username is null ? XlsxCell.Empty
                    : InstagramUsername().IsMatch(username) ? XlsxCell.LinkTo($"@{username}", $"https://instagram.com/{username}")
                    : XlsxCell.Of($"@{username}"),
                .. variableKeys.Select(k => XlsxCell.Of(row.Variables.GetValueOrDefault(k))),
                XlsxCell.Of(string.Join(", ", row.Tags)),
                XlsxCell.At(Local(row.FirstSeenAt, utcOffset)),
                XlsxCell.At(row.LastMessageAt is { } last ? Local(last, utcOffset) : null),
            ];
            if (showAccount)
                line.Add(XlsxCell.Of(row.Channel));
            return (IReadOnlyList<XlsxCell>)line;
        }).ToList();

        List<XlsxColumn> columns =
        [
            new(labels.Number, XlsxColumnKind.Number, Math.Max(5, rows.Count.ToString().Length + 3)),
            new(labels.Name, XlsxColumnKind.Text, Width(labels.Name, rows.Select(r => r.Name), max: 40)),
            new(labels.Instagram, XlsxColumnKind.Link, Width(labels.Instagram, rows.Select(r => r.Username is null ? null : "@" + r.Username), max: 32)),
            .. variableKeys.Select(k =>
            {
                var header = labels.Details.GetValueOrDefault(k) ?? k;
                return new XlsxColumn(header, XlsxColumnKind.Text, Width(header, rows.Select(r => r.Variables.GetValueOrDefault(k)), max: 36));
            }),
            new(labels.Tags, XlsxColumnKind.Text, Width(labels.Tags, rows.Select(r => string.Join(", ", r.Tags)), max: 36)),
            new(labels.FirstSeen, XlsxColumnKind.Date, Math.Max(17, labels.FirstSeen.Length + 3)),
            new(labels.LastMessage, XlsxColumnKind.Date, Math.Max(17, labels.LastMessage.Length + 3)),
        ];
        if (showAccount)
            columns.Add(new(labels.Account, XlsxColumnKind.Text, Width(labels.Account, rows.Select(r => r.Channel), max: 30)));

        // № and the name stay in view while scrolling to the right.
        return XlsxSheet.Build(labels.Sheet, columns, cells, frozenColumns: 2);
    }

    /// <summary>Local time to the minute — what the column shows, so filtering by date is exact.</summary>
    private static DateTime Local(DateTimeOffset value, TimeSpan utcOffset)
    {
        var local = value.ToOffset(utcOffset).DateTime;
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0);
    }

    /// <summary>Wide enough for the longest value (or the header), within reason.</summary>
    private static double Width(string header, IEnumerable<string?> values, int max)
    {
        var longest = values.Select(v => v?.Length ?? 0).DefaultIfEmpty(0).Max();
        return Math.Clamp(Math.Max(longest, header.Length + 3) * 1.1 + 2, 10, max);
    }
}
