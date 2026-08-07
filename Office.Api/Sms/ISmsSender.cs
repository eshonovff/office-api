namespace Office.Api.Sms;

public interface ISmsSender
{
    /// <summary>SMS мефиристад. Дар хатогӣ true намебарорад, истисно намепартояд.</summary>
    Task<bool> SendAsync(string phoneNormalized, string message, CancellationToken ct);
}
