namespace Office.Api.Email;

/// <summary>
/// Fallback-и dev — вақте Email:Resend:ApiKey танзим нашудааст (ниг. Program.cs), email-ро
/// ба log мебарорад, на мефиристад. Ҳамин тавр коди тасдиқ дар консол дида мешавад, бе
/// ҳисоби воқеии Resend низ ҷараёни бақайдгирӣ то охир санҷида мешавад.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<bool> SendAsync(string to, string subject, string bodyText, CancellationToken ct)
    {
        logger.LogWarning(
            "Email:Resend:ApiKey танзим нашудааст — паём ба ҷои фиристодан ба log мебарояд.\nTo: {To}\nSubject: {Subject}\n{Body}",
            to, subject, bodyText);

        return Task.FromResult(true);
    }
}
