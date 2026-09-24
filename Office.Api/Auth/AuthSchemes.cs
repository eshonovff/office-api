namespace Office.Api.Auth;

/// <summary>
/// Номи схемаи дуюми JWT (дар паҳлӯи схемаи пешфарз барои кормандон) — барои Customer
/// (мизоҷи беруна). Дар Program.cs сабт мешавад, дар CustomerAuthEndpoints истифода.
/// </summary>
public static class AuthSchemes
{
    public const string Customer = "Customer";
    public const string CustomerOnlyPolicy = "CustomerOnly";

    /// <summary>
    /// Google/Apple — on та FALSE ID token-и провайдер аст (RS256, аз рӯи JWKS-и худи
    /// Google/Apple тасдиқ мешавад тавассути Authority, на калиди мо). Танҳо вақте сабт
    /// мешаванд, ки Google:ClientId/Apple:ClientId конфигуратсия шуда бошанд (ниг. Program.cs).
    /// </summary>
    public const string Google = "Google";
    public const string Apple = "Apple";
}
