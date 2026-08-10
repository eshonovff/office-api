namespace Office.Api.Common;

public static class UploadsPathResolver
{
    public static string ResolveRootPath(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configured = configuration["Uploads:RootPath"];
        var basePath = configured is { Length: > 0 }
            ? (Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured))
            : Path.Combine(env.ContentRootPath, "uploads");

        return Path.GetFullPath(basePath);
    }
}
