namespace Office.Api.Auth;

/// <summary>
/// Номи схемаи дуюми JWT (дар паҳлӯи схемаи пешфарз барои кормандон) — барои Customer
/// (мизози беруна). Дар Program.cs сабт мешавад, дар CustomerAuthEndpoints истифода.
/// </summary>
public static class AuthSchemes
{
    public const string Customer = "Customer";
    public const string CustomerOnlyPolicy = "CustomerOnly";
}
