namespace Office.Api.Channels;

/// <summary>
/// Матни фаҳмо барои коди сохтории failure_code (ниг. MetaErrorCodeExtractor) — барои намоиши
/// гурӯҳии дашборд, ки танҳо failure_code-и захирашударо дорад, на JSON-и хоми ҳар сатр (ки
/// барои гурӯҳбандӣ дигар дастрас нест — ниг. MetaErrorTranslator барои матни якто-сатра).
/// Коди ношинос — худи код бармегардад (на матни холӣ), то дашборд ҳеҷ гоҳ гурӯҳи "беном" надиҳад.
/// </summary>
public static class FailureCodeLabels
{
    // Мушаххас — провайдер+code+subcode якҷоя (санҷида зинда, ниг. report 2026-08-26).
    // IG_1 қасдан ин ҷо НЕСТ — то ҳол як маротиба дида шудааст, кофӣ нест барои хулоса
    // (ниг. report 2026-08-26, А2). Худи код нишон дода мешавад (поён), ва ҳар бори нав
    // логи Warning бо payload-и пурра мегузорад (ниг. MediaSendJob/WhatsAppSendJob).
    private static readonly Dictionary<string, string> ExactLabels = new()
    {
        ["FB_100_2018074"] = "Meta файли фиристодашударо зеркашӣ карда натавонист — хатои муваққатии тарафи Meta.",
        ["IG_2"] = "Хидмати Meta муваққатан дастрас нест (тасдиқшуда: банди App Review-и Instagram).",
    };

    // Универсалӣ — новобаста аз провайдер, ниг. MetaErrorTranslator барои ҳамин рамзҳо.
    private static readonly Dictionary<string, string> CodeSuffixLabels = new()
    {
        ["_131030"] = "Рақами гиранда дар рӯйхати иҷозатдодашуда нест.",
        ["_190"] = "Токен аз эътибор соқит шуд — каналро аз нав пайваст кунед.",
        ["_131047"] = "Тирезаи 24-соата баста аст.",
        ["_470"] = "Тирезаи 24-соата баста аст.",
    };

    public static string Label(string failureCode)
    {
        if (ExactLabels.TryGetValue(failureCode, out var exact))
            return exact;

        foreach (var (suffix, text) in CodeSuffixLabels)
        {
            if (failureCode.EndsWith(suffix, StringComparison.Ordinal))
                return text;
        }

        return failureCode;
    }

    /// <summary>
    /// Барои А2 (ниг. report): вақте failure_code сабт мешавад, агар он ин ҷо ношинос бошад,
    /// caller (MediaSendJob/WhatsAppSendJob) бояд логи Warning бо payload-и пурра гузорад —
    /// то дафъаи оянда маълумот дошта бошем, на боз як "як маротиба, кор накун".
    /// </summary>
    public static bool IsKnown(string failureCode) =>
        ExactLabels.ContainsKey(failureCode) || CodeSuffixLabels.Keys.Any(suffix => failureCode.EndsWith(suffix, StringComparison.Ordinal));
}
