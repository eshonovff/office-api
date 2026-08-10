using Office.Api.Common;

namespace Office.Api.Tests.Common;

public class SafeUploadsPathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "office-api-tests-uploads-root");

    [Fact]
    public void TryResolve_NormalRelativePath_ResolvesInsideRoot()
    {
        var result = SafeUploadsPath.TryResolve(Root, "whatsapp-media/abc/def.jpg");

        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(Root), result);
        Assert.EndsWith(Path.Combine("whatsapp-media", "abc", "def.jpg"), result);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../../../../../../etc/passwd")]
    [InlineData("whatsapp-media/../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("whatsapp-media/..")]
    public void TryResolve_TraversalAttempt_ReturnsNull(string maliciousRelativePath)
    {
        Assert.Null(SafeUploadsPath.TryResolve(Root, maliciousRelativePath));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/var/office/uploads-evil/file")]
    public void TryResolve_AbsolutePath_ReturnsNull(string absolutePath)
    {
        Assert.Null(SafeUploadsPath.TryResolve(Root, absolutePath));
    }

    [Fact]
    public void TryResolve_SiblingDirectoryPrefixBypass_ReturnsNull()
    {
        // Resolves to a sibling directory whose name starts with the same characters as the
        // root (e.g. "<root>-evil") — a naive StartsWith(root) without a trailing separator
        // would wrongly accept this even though it's outside the root.
        var relativePath = $"../{Path.GetFileName(Root)}-evil/file.jpg";

        Assert.Null(SafeUploadsPath.TryResolve(Root, relativePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolve_NullOrEmpty_ReturnsNull(string? relativePath)
    {
        Assert.Null(SafeUploadsPath.TryResolve(Root, relativePath));
    }
}
