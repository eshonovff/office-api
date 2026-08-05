using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Office.Api.Common;

/// <summary>
/// Ба ҳуҷҷати OpenAPI securityScheme-и Bearer илова мекунад, то дар Scalar
/// тугмаи "Authorize" пайдо шавад ва токен ба ҳамаи request худаш замима шавад.
/// </summary>
public class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (schemes.All(s => s.Name != JwtBearerDefaults.AuthenticationScheme))
            return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token аз /api/auth/login. Масалан: `Bearer eyJhbGciOi...`",
        };

        var bearerReference = new OpenApiSecuritySchemeReference("Bearer", document);

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations!.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement { [bearerReference] = [] });
            }
        }
    }
}
