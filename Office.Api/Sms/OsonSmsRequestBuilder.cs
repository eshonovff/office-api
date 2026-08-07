using System.Security.Cryptography;
using System.Text;

namespace Office.Api.Sms;

/// <summary>
/// Сохтани дархости OsonSMS (pure, бе HTTP) — то тавон онро бе шабака тест кард.
/// Мутобиқ ба протоколи "sendsms_v1.php": str_hash = SHA256(txn_id;login;sender;phone;secretHash).
/// </summary>
public static class OsonSmsRequestBuilder
{
    public static string BuildHash(string txnId, string login, string sender, string phoneLocal, string secretHash)
    {
        var raw = string.Join(';', txnId, login, sender, phoneLocal, secretHash);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }

    public static string BuildUrl(
        string serverUrl, string login, string sender, string phoneLocal, string message, string txnId, string secretHash)
    {
        var strHash = BuildHash(txnId, login, sender, phoneLocal, secretHash);

        var query = string.Join('&',
            $"from={Uri.EscapeDataString(sender)}",
            $"phone_number={Uri.EscapeDataString(phoneLocal)}",
            $"msg={Uri.EscapeDataString(message)}",
            $"str_hash={strHash}",
            $"txn_id={Uri.EscapeDataString(txnId)}",
            $"login={Uri.EscapeDataString(login)}");

        var separator = serverUrl.Contains('?') ? '&' : '?';
        return $"{serverUrl}{separator}{query}";
    }
}
