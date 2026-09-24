namespace Office.Api.Email;

public interface IEmailSender
{
    /// <summary>Email мефиристад. Дар хатогӣ true намебарорад, истисно намепартояд.</summary>
    Task<bool> SendAsync(string to, string subject, string bodyText, CancellationToken ct);
}
