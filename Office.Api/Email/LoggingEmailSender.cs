namespace Office.Api.Email;

/// <summary>
/// Fallback-и dev — вақте Email:Resend:ApiKey танзим нашудааст (ниг. Program.cs), email-ро
/// ба log мебарорад, на мефиристад. Ҳамин тавр коди тасдиқ дар консол дида мешавад, бе
/// ҳисоби воқеии Resend низ ҷараёни бақайдгирӣ то охир санҷида мешавад.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task<bool> SendAsync(string to, string subject, string bodyText, CancellationToken ct)
    {
        // Outside Development the body is never logged: it can hold a verification code or a
        // password reset link, and a forgotten Resend key must not put those in the logs.
        if (!environment.IsDevelopment())
        {
            logger.LogError("Email:Resend:ApiKey танзим нашудааст — email фиристода нашуд ({Subject}).", subject);
            return Task.FromResult(false);
        }

        logger.LogWarning(
            "Email:Resend:ApiKey танзим нашудааст — паём ба ҷои фиристодан ба log мебарояд.\nTo: {To}\nSubject: {Subject}\n{Body}",
            to, subject, bodyText);

        return Task.FromResult(true);
    }
}
