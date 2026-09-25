namespace Office.Api.Features.Subscriptions;

/// <summary>
/// How long a customer has, after getting an amount, to pay and upload the receipt
/// (Subscriptions:PaymentWindowMinutes). Past it the request expires and its amount is freed.
/// </summary>
public static class PaymentWindow
{
    /// <summary>
    /// Extra time the upload endpoint allows past the deadline the customer sees: a receipt
    /// picked at 4:59 on a slow mobile connection can reach the server well after 5:00.
    /// </summary>
    public static readonly TimeSpan UploadGrace = TimeSpan.FromMinutes(1);

    public static DateTimeOffset DeadlineFor(DateTimeOffset createdAt, TimeSpan window) => createdAt + window;

    /// <summary>For listings and new requests — the deadline exactly as the customer saw it.</summary>
    public static bool IsPastDeadline(DateTimeOffset createdAt, TimeSpan window, DateTimeOffset now) =>
        now > DeadlineFor(createdAt, window);

    /// <summary>For the upload itself — the deadline plus <see cref="UploadGrace"/>.</summary>
    public static bool IsTooLateToUpload(DateTimeOffset createdAt, TimeSpan window, DateTimeOffset now) =>
        now > DeadlineFor(createdAt, window) + UploadGrace;
}
