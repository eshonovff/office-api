using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class ClientErrorClassifierTests
{
    [Fact]
    public void IsClientInputError_BadHttpRequestException_ReturnsTrue()
    {
        // Минимал APIs body binding-и вайроншударо (масалан DueDate-и бидуни формат) ба ин мепечонад.
        Assert.True(ClientErrorClassifier.IsClientInputError(new BadHttpRequestException("bad body")));
    }

    [Fact]
    public void IsClientInputError_JsonException_ReturnsTrue()
    {
        Assert.True(ClientErrorClassifier.IsClientInputError(new JsonException("bad json")));
    }

    [Fact]
    public void IsClientInputError_OtherException_ReturnsFalse()
    {
        Assert.False(ClientErrorClassifier.IsClientInputError(new InvalidOperationException("db down")));
    }
}
