using Office.Api.Common;

namespace Office.Api.Features.Subscriptions;

/// <summary>
/// Receipts live under uploads/subscription-receipts/{customerId}/ and are never served
/// publicly — only through the moderator endpoint gated by subscriptions.manage.
/// </summary>
internal static class SubscriptionReceiptStorage
{
    private const long MaxSizeBytes = 10 * 1024 * 1024;
    private const string Folder = "subscription-receipts";

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };

    public static IResult? Validate(IFormFile file)
    {
        if (file.Length <= 0)
        {
            return Results.Problem(
                title: "Файл холӣ аст",
                detail: "Лутфан скриншоти чекро интихоб кунед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxSizeBytes)
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: "Ҳаҷми чек набояд аз 10 МБ зиёд бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            return NotAnImageOrPdf();

        // The name says png — do the bytes? (A renamed HTML page must never reach a moderator.)
        Span<byte> header = stackalloc byte[ReceiptFileSignature.HeaderLength];
        using var stream = file.OpenReadStream();
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (!ReceiptFileSignature.Matches(extension, header[..read]))
            return NotAnImageOrPdf();

        return null;
    }

    private static IResult NotAnImageOrPdf() => Results.Problem(
        title: "Навъи файл иҷозат дода нашудааст",
        detail: "Танҳо расм (jpg, png, webp) ё PDF.",
        statusCode: StatusCodes.Status400BadRequest);

    public static async Task<string> SaveAsync(string rootPath, Guid customerId, IFormFile file, CancellationToken ct)
    {
        var folder = Path.Combine(rootPath, Folder, customerId.ToString());
        Directory.CreateDirectory(folder);

        var storedFileName = $"{Guid.CreateVersion7()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using (var stream = File.Create(Path.Combine(folder, storedFileName)))
            await file.CopyToAsync(stream, ct);

        return Path.Combine(Folder, customerId.ToString(), storedFileName);
    }

    public static void DeleteIfExists(string rootPath, string? relativePath)
    {
        var fullPath = SafeUploadsPath.TryResolve(rootPath, relativePath);
        if (fullPath is not null && File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
