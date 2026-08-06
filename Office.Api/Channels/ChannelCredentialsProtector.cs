using Microsoft.AspNetCore.DataProtection;

namespace Office.Api.Channels;

public interface IChannelCredentialsProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

/// <summary>
/// Шифрбандии credentials-и канал (токен, App Secret ва ғ.) бо Data Protection API.
/// Матни оддӣ ҳеҷ гоҳ дар база сабт намешавад.
/// </summary>
public class ChannelCredentialsProtector : IChannelCredentialsProtector
{
    private const string Purpose = "Office.Api.Channels.Credentials.v1";

    private readonly IDataProtector _protector;

    public ChannelCredentialsProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
