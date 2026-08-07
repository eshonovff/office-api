using System.Security.Cryptography;

namespace Office.Api.Common;

public static class PasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
    private const string NumericAlphabet = "0123456789";

    public static string Generate(int length = 12)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];

        return new string(chars);
    }

    /// <summary>Пароли рақамии кӯтоҳ — барои SMS осонтар аз телефон дохил кардан.</summary>
    public static string GenerateNumeric(int length = 8)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = NumericAlphabet[bytes[i] % NumericAlphabet.Length];

        return new string(chars);
    }
}
