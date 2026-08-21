using System.Text;
using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class HtmlContentSnifferTests
{
    [Theory]
    [InlineData("<!doctype html><html><body>Link expired</body></html>")]
    [InlineData("<!DOCTYPE HTML><html><head></head></html>")]
    [InlineData("  \n  <html><body>error</body></html>")]
    public void LooksLikeHtml_HtmlContent_ReturnsTrue(string html)
    {
        Assert.True(HtmlContentSniffer.LooksLikeHtml(Encoding.UTF8.GetBytes(html)));
    }

    [Fact]
    public void LooksLikeHtml_RealBinaryContent_ReturnsFalse()
    {
        byte[] jpegLikeBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        Assert.False(HtmlContentSniffer.LooksLikeHtml(jpegLikeBytes));
    }

    [Fact]
    public void LooksLikeHtml_EmptyContent_ReturnsFalse()
    {
        Assert.False(HtmlContentSniffer.LooksLikeHtml([]));
    }

    [Fact]
    public void LooksLikeHtml_PlainTextThatIsNotHtml_ReturnsFalse()
    {
        Assert.False(HtmlContentSniffer.LooksLikeHtml(Encoding.UTF8.GetBytes("just some random bytes, not markup")));
    }
}
