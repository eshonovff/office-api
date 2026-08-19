using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Instagram API with Instagram Login App-и АЛОҲИДА аст (ID/Secret-и худ, аз app-и асосии
/// Meta фарқ мекунад — ниг. <see cref="Meta.MetaOAuthConfig"/>). Аз ин рӯ webhook-и Instagram
/// <c>X-Hub-Signature-256</c>-ро бо App Secret-и ХУДИ ХУД имзо мекунад, на бо secret-и
/// app-и асосӣ, ки WhatsApp ва Facebook истифода мебаранд. Санҷидан бо secret-и нодуруст
/// ҳамеша хомӯшона рад мешавад (имзо мувофиқ намеояд) — ин интихоб бояд ошкоро бошад.
/// </summary>
public static class WebhookAppSecretSelector
{
    public static bool UseInstagramAppSecret(ChannelType provider) => provider == ChannelType.Instagram;
}
