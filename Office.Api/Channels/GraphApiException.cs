namespace Office.Api.Channels;

/// <summary>
/// Meta Graph API ғайри-2xx баргардонд. Message (аз Exception) — матни аллакай тарҷумашудаи
/// MetaErrorTranslator (инсонфаҳм, кӯтоҳ); RawResponseBody — JSON-и хоми Meta, барои debug
/// (Message.FailureDetail → frontend-и details/tooltip-и пӯшида), ҳеҷ гоҳ мустақим дар ҳубоб
/// чоп намешавад.
/// </summary>
public sealed class GraphApiException(string userMessage, string rawResponseBody) : Exception(userMessage)
{
    public string RawResponseBody { get; } = rawResponseBody;
}
