namespace Office.Api.Common;

/// <summary>
/// Ҳалли бехатари роҳи нисбии захирашуда (мас. `Message.MediaUrl`) нисбат ба решаи
/// uploads — pure, бе File I/O. Ҳеҷ гоҳ натиҷаи `Path.Combine` бе санҷиш истифода
/// набар: `..`-и такрор ё роҳи мутлақ метавонад аз реша барояд (path traversal).
/// </summary>
public static class SafeUploadsPath
{
    /// <summary>
    /// Роҳи пурраро бармегардонад агар дар дохили <paramref name="rootPath"/> монад,
    /// вагарна `null` (роҳи мутлақ ё `..`-и берунбаранда).
    /// </summary>
    public static string? TryResolve(string rootPath, string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var normalizedRoot = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));

        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? candidate : null;
    }
}
