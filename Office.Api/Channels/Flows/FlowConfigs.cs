namespace Office.Api.Channels.Flows;

/// <summary>
/// Як блоки паём — матн то 1000 аломат (ҳудуди Instagram; санҷида дар FlowsEndpoints.ValidateNodeConfig).
/// Variants (танҳо блоки матн, ихтиёрӣ): матнҳои дигари ҳамон паём — бот ба ҳар корбар якеро аз
/// Text ва Variants мефиристад (MessageTextPicker), то як матни якхела ба ҳама спам наменамояд.
/// PreviewDataUri (ихтиёрӣ): thumbnail-и хурди "data:image/jpeg;base64,..." — MediaId (attachment_id-и
/// Meta) баъд аз reload аз он расм бозгашт кардан НАМЕШАВАД (опаку, GET-и оммавӣ надорад), пас ин
/// thumbnail-и мустақил дар ҳамин JSON захира мешавад, то панел/canvas пас аз reload низ расмро
/// нишон диҳанд — ниг. FlowsEndpoints.UploadMediaAsync ва FlowTemplateInstantiator.AttachDefaultImagesAsync.
/// </summary>
public record MessageBlock(string Type, string? Text, string? MediaId, string? PreviewDataUri = null, string[]? Variants = null)
{
    /// <summary>Instagram's limit for one message's text.</summary>
    public const int MaxTextLength = 1000;
    /// <summary>Besides Text — five texts in all, as for the public replies under a comment.</summary>
    public const int MaxVariants = 4;

    public const string TypeText = "text";
    public const string TypeImage = "image";
    public const string TypeVideo = "video";
    public const string TypeAudio = "audio";
    public const string TypeFile = "file";
}

/// <summary>
/// Title то 30 аломат (маҳдудияти Messenger Platform). Action="payment" дар JSON қабул карда
/// мешавад (мутобиқат бо намуди спека), вале дар UI пешниҳод НАМЕШАВАД ва дар backend рад
/// мешавад — спека худаш "маҳсулоти пулакӣ"-ро дар рӯйхати НАГИР дорад, ин зиддият ба фоидаи
/// НАГИР ҳал шуд (ниг. ҳуҷҷати фазаи 12).
/// </summary>
public record MessageButton(string Title, string Action, string? Url, bool AllowRepeat)
{
    public const string ActionNext = "next";
    public const string ActionUrl = "url";
}

public record MessageNodeConfig(MessageBlock[] Blocks, MessageButton[] Buttons);

public record ConditionRule(string Field, string Op, string Value)
{
    public const string FieldSubscription = "subscription";
    public const string FieldTags = "tags";
    public const string FieldVariable = "variable";
    public const string FieldTime = "time";
    public const string FieldDate = "date";
    public const string FieldWeekday = "weekday";
}

public record ConditionNodeConfig(string Match, ConditionRule[] Rules)
{
    public const string MatchAll = "all";
    public const string MatchAny = "any";
}

/// <summary>
/// Kind муайян мекунад кадом майдонҳо истифода мешаванд — ҳамон алгуи ҳамворкунии
/// AutomationActionConfig-и Фазаи 10/11 (record-и ягона, на иерархияи полиморфӣ, чунки ин
/// лоиҳа ҳеҷ ҷо полиморфизми JSON истифода намебарад).
/// </summary>
public record ActionNodeConfig(
    string Kind,
    int? DelayMinutes = null,
    string[]? Tags = null,
    string? VariableKey = null,
    string? VariableValue = null,
    string? HttpUrl = null,
    string? HttpMethod = null,
    string? HttpBodyTemplate = null,
    Guid? TargetFlowId = null)
{
    public const string KindDelay = "delay";
    public const string KindAddTags = "add_tags";
    public const string KindRemoveTags = "remove_tags";
    public const string KindSetVariable = "set_variable";
    public const string KindCollectInput = "collect_input";
    public const string KindHttpRequest = "http_request";
    public const string KindGotoFlow = "goto_flow";
}

public record NoteNodeConfig(string Text);
