using System.Text;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class ReceiptFileSignatureTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBP"u8];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n%"u8, 0xE2, 0xE3];

    [Fact]
    public void RealFiles_Match_TheirExtension()
    {
        Assert.True(ReceiptFileSignature.Matches(".jpg", Jpeg));
        Assert.True(ReceiptFileSignature.Matches(".JPEG", Jpeg));
        Assert.True(ReceiptFileSignature.Matches(".png", Png));
        Assert.True(ReceiptFileSignature.Matches(".webp", Webp));
        Assert.True(ReceiptFileSignature.Matches(".pdf", Pdf));
    }

    [Theory]
    [InlineData(".png", "<!doctype html><script>alert(1)</script>")]
    [InlineData(".png", "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
    [InlineData(".pdf", "<html><body>not a pdf</body></html>")]
    [InlineData(".jpg", "GIF89a......")]
    public void RenamedPages_AreRefused(string extension, string content)
    {
        Assert.False(ReceiptFileSignature.Matches(extension, Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void RealImage_UnderTheWrongExtension_IsRefused()
    {
        Assert.False(ReceiptFileSignature.Matches(".jpg", Png));
        Assert.False(ReceiptFileSignature.Matches(".pdf", Jpeg));
    }

    [Fact]
    public void EmptyOrTruncated_IsRefused()
    {
        Assert.False(ReceiptFileSignature.Matches(".png", []));
        Assert.False(ReceiptFileSignature.Matches(".webp", "RIFF"u8.ToArray()));
    }

    [Fact]
    public void OtherExtensions_AreRefused()
    {
        Assert.False(ReceiptFileSignature.Matches(".svg", Png));
        Assert.False(ReceiptFileSignature.Matches(".html", Png));
    }
}
