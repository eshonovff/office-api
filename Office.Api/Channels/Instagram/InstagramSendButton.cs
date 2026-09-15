namespace Office.Api.Channels.Instagram;

/// <summary>Тугмаи фиристодашаванда — ниг. InstagramProvider.SendButtonMessageAsync.</summary>
public record InstagramSendButton(string Title, string Type, string? Url, string? Payload)
{
    public const string TypeWebUrl = "web_url";
    public const string TypePostback = "postback";
}
