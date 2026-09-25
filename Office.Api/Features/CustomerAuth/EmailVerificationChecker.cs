namespace Office.Api.Features.CustomerAuth;

public enum EmailVerificationResult
{
    Ok,
    AlreadyVerified,
    NoCodeRequested,
    Expired,
    TooManyAttempts,
    CodeMismatch,
}

/// <summary>
/// Қарори "оё ин код қабул шавад" — pure, бе DB, то ҷудо тест кард. CustomerAuthEndpoints
/// танҳо far ин натиҷаро ба амал (сабт кардани EmailVerifiedAt, зиёд кардани attempts, ё
/// паёми хатои умумӣ) мегузаронад.
/// </summary>
public static class EmailVerificationChecker
{
    public const int MaxAttempts = 5;

    public static EmailVerificationResult Check(
        bool alreadyVerified,
        string? storedCodeHash,
        DateTimeOffset? expiresAt,
        int attempts,
        DateTimeOffset now,
        string submittedCodeHash)
    {
        if (alreadyVerified)
            return EmailVerificationResult.AlreadyVerified;

        if (storedCodeHash is null || expiresAt is null)
            return EmailVerificationResult.NoCodeRequested;

        if (attempts >= MaxAttempts)
            return EmailVerificationResult.TooManyAttempts;

        if (now > expiresAt.Value)
            return EmailVerificationResult.Expired;

        return storedCodeHash == submittedCodeHash
            ? EmailVerificationResult.Ok
            : EmailVerificationResult.CodeMismatch;
    }
}
