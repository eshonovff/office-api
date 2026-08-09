namespace Office.Api.Features.Legal;

public static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/privacy", () => Results.Content(PrivacyPolicyHtml, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private const string PrivacyPolicyHtml = """
        <!DOCTYPE html>
        <html lang="tg">
        <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>Сиёсати махфият — office.nizom.tj</title>
        <style>
        body { font-family: -apple-system, Arial, sans-serif; max-width: 640px; margin: 40px auto; padding: 0 16px; line-height: 1.6; color: #1a1a1a; }
        h1 { font-size: 1.4rem; }
        ul { padding-inline-start: 20px; }
        a { color: #0066cc; }
        </style>
        </head>
        <body>
        <h1>Сиёсати махфият</h1>
        <p><strong>office.nizom.tj</strong> платформаи дохилии ширкати SMARTWEB TJ аст.</p>
        <ul>
        <li>Танҳо кормандони ширкат ба ин система дастрасӣ доранд.</li>
        <li>Паёмҳои мижозон аз WhatsApp, Instagram ва Facebook барои коркарди дархостҳо ҷамъ ва нигоҳ дошта мешаванд.</li>
        <li>Маълумот ба тарафи сеюм дода ё фурӯхта намешавад.</li>
        </ul>
        <p>Барои нест кардани маълумот тамос гиред: <a href="mailto:dev@nizom.tj">dev@nizom.tj</a></p>
        </body>
        </html>
        """;
}
