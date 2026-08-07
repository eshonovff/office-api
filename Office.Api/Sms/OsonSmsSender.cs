using System.Text.Json;
using Office.Api.Common;

namespace Office.Api.Sms;

/// <summary>Фиристодани SMS тавассути OsonSMS (https://osonsms.com). Дар хатогӣ истисно намепартояд.</summary>
public class OsonSmsSender(HttpClient httpClient, IConfiguration configuration, ILogger<OsonSmsSender> logger) : ISmsSender
{
    public async Task<bool> SendAsync(string phoneNormalized, string message, CancellationToken ct)
    {
        var login = configuration["Sms:Login"];
        var hash = configuration["Sms:Hash"];
        var sender = configuration["Sms:Sender"];
        var serverUrl = configuration["Sms:ServerUrl"];

        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(hash) ||
            string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(serverUrl))
        {
            logger.LogError("Sms:Login/Hash/Sender/ServerUrl конфигуратсия нашудааст — SMS фиристода нашуд.");
            return false;
        }

        var phoneLocal = PhoneNumber.ToLocalDigits(phoneNormalized);
        var txnId = Guid.CreateVersion7().ToString();
        var url = OsonSmsRequestBuilder.BuildUrl(serverUrl, login, sender, phoneLocal, message, txnId, hash);

        try
        {
            var response = await httpClient.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OsonSMS HTTP {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                logger.LogWarning("OsonSMS хатогӣ баргардонд: {Error}", error.GetRawText());
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Дархости OsonSMS ноком шуд.");
            return false;
        }
    }
}
