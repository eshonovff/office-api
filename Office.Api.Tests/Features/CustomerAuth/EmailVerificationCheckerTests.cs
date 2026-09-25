using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class EmailVerificationCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private const string CorrectHash = "correct-hash";
    private const string WrongHash = "wrong-hash";

    [Fact]
    public void Check_AlreadyVerified_ReturnsAlreadyVerified()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: true,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(10),
            attempts: 0,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.AlreadyVerified, result);
    }

    [Fact]
    public void Check_NoCodeEverRequested_ReturnsNoCodeRequested()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: null,
            expiresAt: null,
            attempts: 0,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.NoCodeRequested, result);
    }

    [Fact]
    public void Check_MaxAttemptsReached_ReturnsTooManyAttempts_EvenWithCorrectCode()
    {
        // Ҳатто агар охирин код дуруст бошад ҳам — attempts-и қаблӣ аллакай ба ҳад расидааст.
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(10),
            attempts: EmailVerificationChecker.MaxAttempts,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.TooManyAttempts, result);
    }

    [Fact]
    public void Check_AttemptsOneBelowMax_StillAllowed()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(10),
            attempts: EmailVerificationChecker.MaxAttempts - 1,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.Ok, result);
    }

    [Fact]
    public void Check_CodeExpired_ReturnsExpired()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(-1),
            attempts: 0,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.Expired, result);
    }

    [Fact]
    public void Check_ExactlyAtExpiry_StillValid()
    {
        // now > expiresAt лозим аст барои "гузашт" — баробарӣ ҳанӯз дурустӣ мебошад.
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now,
            attempts: 0,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.Ok, result);
    }

    [Fact]
    public void Check_WrongCode_ReturnsCodeMismatch()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(10),
            attempts: 0,
            now: Now,
            submittedCodeHash: WrongHash);

        Assert.Equal(EmailVerificationResult.CodeMismatch, result);
    }

    [Fact]
    public void Check_CorrectCodeWithinWindow_ReturnsOk()
    {
        var result = EmailVerificationChecker.Check(
            alreadyVerified: false,
            storedCodeHash: CorrectHash,
            expiresAt: Now.AddMinutes(10),
            attempts: 2,
            now: Now,
            submittedCodeHash: CorrectHash);

        Assert.Equal(EmailVerificationResult.Ok, result);
    }
}
