namespace Office.Api.Common;

public static class AppUrls
{
    /// <summary>
    /// Where the web app is served ("App:PublicUrl") — for links in emails. Always from
    /// configuration, never from the incoming request's Host header.
    /// </summary>
    public static string GetPublicUrl(IConfiguration configuration) =>
        configuration["App:PublicUrl"]?.TrimEnd('/')
        ?? throw new InvalidOperationException("App:PublicUrl танзим нашудааст.");
}
