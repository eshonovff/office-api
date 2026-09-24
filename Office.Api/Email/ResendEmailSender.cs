using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Office.Api.Email;

/// <summary>Фиристодани email тавассути Resend (https://resend.com). Дар хатогӣ истисно намепартояд.</summary>
public class ResendEmailSender(HttpClient httpClient, IConfiguration configuration, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(string to, string subject, string bodyText, CancellationToken ct)
    {
        var apiKey = configuration["Email:Resend:ApiKey"];
        var from = configuration["Email:Resend:FromAddress"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(from))
        {
            logger.LogError("Email:Resend:ApiKey/FromAddress конфигуратсия нашудааст — email фиристода нашуд.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new { from, to = new[] { to }, subject, text = bodyText });

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Resend HTTP {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Дархости Resend ноком шуд.");
            return false;
        }
    }
}
