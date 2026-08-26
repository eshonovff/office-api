using System.Text;

namespace Office.Api.Media;

/// <summary>
/// Файли аллакай дар диск буда воқеан HTML аст ё не — pure, бе DB/HTTP. Барои тозакунии
/// файлҳое, ки ПЕШ аз MediaContentTypeValidator илова шуданаш "муваффақ" сабт шуда буданд
/// (масалан 52 файли HTML-и канали Instagram).
/// </summary>
public static class HtmlContentSniffer
{
    private const int SampleLength = 512;

    public static bool LooksLikeHtml(ReadOnlySpan<byte> content)
    {
        var sample = content.Length > SampleLength ? content[..SampleLength] : content;
        var text = Encoding.UTF8.GetString(sample).TrimStart();

        return text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }
}
