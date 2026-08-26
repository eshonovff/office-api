using Office.Api.Channels;

namespace Office.Api.Tests.Channels;

public class MetaErrorTranslatorTests
{
    private static string ErrorJson(int code, bool isTransient = false, string message = "some error") =>
        $"{{\"error\":{{\"message\":\"{message}\",\"type\":\"OAuthException\",\"code\":{code},\"is_transient\":{isTransient.ToString().ToLowerInvariant()},\"fbtrace_id\":\"abc123\"}}}}";

    [Fact]
    public void Translate_RecipientNotInAllowedList_ReturnsSpecificMessage()
    {
        var result = MetaErrorTranslator.Translate(ErrorJson(131030));

        Assert.Equal("Рақами гиранда дар рӯйхати иҷозатдодашуда нест.", result);
    }

    [Fact]
    public void Translate_TokenExpired_ReturnsSpecificMessage()
    {
        var result = MetaErrorTranslator.Translate(ErrorJson(190));

        Assert.Equal("Токен аз эътибор соқит шуд — каналро аз нав пайваст кунед.", result);
    }

    [Theory]
    [InlineData(131047)]
    [InlineData(470)]
    public void Translate_WindowClosedCodes_ReturnSameMessage(int code)
    {
        var result = MetaErrorTranslator.Translate(ErrorJson(code));

        Assert.Equal("Тирезаи 24-соата баста аст.", result);
    }

    [Fact]
    public void Translate_TransientServiceCode2_ReturnsTransientMessage()
    {
        var result = MetaErrorTranslator.Translate(ErrorJson(2));

        Assert.Equal("Хидмати Meta муваққатан дастрас нест.", result);
    }

    [Fact]
    public void Translate_UnknownCodeButIsTransientTrue_ReturnsTransientMessage()
    {
        // A code we've never seen before, but Meta itself flagged it as transient —
        // trust that flag over falling back to the fully generic message.
        var result = MetaErrorTranslator.Translate(ErrorJson(999999, isTransient: true));

        Assert.Equal("Хидмати Meta муваққатан дастрас нест.", result);
    }

    [Fact]
    public void Translate_UnknownCodeNotTransient_ReturnsGenericMessage()
    {
        var result = MetaErrorTranslator.Translate(ErrorJson(999999));

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Fact]
    public void Translate_MalformedJson_ReturnsGenericMessage()
    {
        var result = MetaErrorTranslator.Translate("{not valid json at all");

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Fact]
    public void Translate_JsonWithoutErrorObject_ReturnsGenericMessage()
    {
        var result = MetaErrorTranslator.Translate("""{"unexpected":"shape"}""");

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Fact]
    public void Translate_ErrorObjectWithoutCode_ReturnsGenericMessage()
    {
        var result = MetaErrorTranslator.Translate("""{"error":{"message":"something broke"}}""");

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Translate_NullOrEmptyOrWhitespace_ReturnsGenericMessage(string? rawResponseBody)
    {
        var result = MetaErrorTranslator.Translate(rawResponseBody);

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Fact]
    public void Translate_ErrorIsNotAnObject_ReturnsGenericMessage()
    {
        var result = MetaErrorTranslator.Translate("""{"error":"just a string, not an object"}""");

        Assert.Equal("Паём фиристода нашуд.", result);
    }

    [Fact]
    public void Translate_NeverReturnsTheRawJsonItself()
    {
        // The whole point of this translator: no matter the input, the raw JSON never leaks
        // into the returned text — that's what used to overflow the message bubble.
        var raw = ErrorJson(131030, message: "a very long raw diagnostic string from Meta");
        var result = MetaErrorTranslator.Translate(raw);

        Assert.DoesNotContain("fbtrace_id", result);
        Assert.DoesNotContain("{", result);
    }
}
