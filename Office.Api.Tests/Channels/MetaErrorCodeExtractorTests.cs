using Office.Api.Channels;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels;

public class MetaErrorCodeExtractorTests
{
    // Се шакли воқеии ҷавоби хато — рост аз messages.failure_reason-и production гирифта
    // шуда (пеш аз FailureDetail вуҷуд дошт, пас "{Provider} Graph API хатогӣ: " пешванд
    // дорад — ниг. report). Extract() бояд ҳарду шаклро (пешвандор ва холис) якхела кор кунад.
    private const string FacebookRealFixture =
        """Facebook Graph API хатогӣ: {"error":{"message":"(#100) Не удалось скачать вложение с помощью его ID.","type":"OAuthException","code":100,"error_subcode":2018074,"fbtrace_id":"A1iUbI17pLZ0IK1wumTkPo9"}}""";

    private const string InstagramRealFixture =
        """Instagram Graph API хатогӣ: {"error":{"message":"Service temporarily unavailable","type":"IGApiException","is_transient":true,"code":2,"fbtrace_id":"Advi6H5_by6H9ojJ13auKiH"}}""";

    private const string WhatsAppRealFixture =
        """WhatsApp Graph API хатогӣ: {"error":{"message":"(#131030) Recipient phone number not in allowed list","code":131030,"type":"OAuthException","error_data":{"messaging_product":"whatsapp","details":"..."},"fbtrace_id":"AeBztVd6eSjcEUBG4VUOFKm"}}""";

    [Fact]
    public void Extract_RealFacebookFixture_ReturnsProviderCodeAndSubcode()
    {
        Assert.Equal("FB_100_2018074", MetaErrorCodeExtractor.Extract(ChannelType.Facebook, FacebookRealFixture));
    }

    [Fact]
    public void Extract_RealInstagramFixture_ReturnsProviderAndCodeOnly()
    {
        Assert.Equal("IG_2", MetaErrorCodeExtractor.Extract(ChannelType.Instagram, InstagramRealFixture));
    }

    [Fact]
    public void Extract_RealWhatsAppFixture_ReturnsProviderAndCodeOnly()
    {
        Assert.Equal("WA_131030", MetaErrorCodeExtractor.Extract(ChannelType.WhatsApp, WhatsAppRealFixture));
    }

    [Fact]
    public void Extract_CleanJsonWithoutPrefix_WorksTheSameAsPrefixedLegacyText()
    {
        // Production-и оянда (баъд аз FailureDetail): JSON холис, бе "{Provider} Graph API
        // хатогӣ: " пешванд.
        const string cleanJson = """{"error":{"message":"...","code":100,"error_subcode":2018074}}""";

        Assert.Equal("FB_100_2018074", MetaErrorCodeExtractor.Extract(ChannelType.Facebook, cleanJson));
    }

    [Fact]
    public void Extract_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, "{not valid json"));
    }

    [Fact]
    public void Extract_ResponseWithoutErrorObject_ReturnsNull()
    {
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, """{"data":[]}"""));
    }

    [Fact]
    public void Extract_ErrorObjectWithoutCode_ReturnsNull()
    {
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, """{"error":{"message":"something broke"}}"""));
    }

    [Fact]
    public void Extract_CodeWithoutSubcode_OmitsSubcodeSegment()
    {
        Assert.Equal("WA_190", MetaErrorCodeExtractor.Extract(ChannelType.WhatsApp, """{"error":{"code":190}}"""));
    }

    [Fact]
    public void Extract_NullOrEmptyInput_ReturnsNull()
    {
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, null));
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, ""));
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, "   "));
    }

    [Fact]
    public void Extract_NoOpeningBraceAnywhere_ReturnsNull()
    {
        Assert.Null(MetaErrorCodeExtractor.Extract(ChannelType.Facebook, "connection reset by peer"));
    }
}
