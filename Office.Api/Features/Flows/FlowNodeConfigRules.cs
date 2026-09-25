using System.Text.Json;
using Office.Api.Channels.Flows;

namespace Office.Api.Features.Flows;

/// <summary>Checks each node's typed config on a graph save (moved out of FlowsEndpoints to be testable).</summary>
public static class FlowNodeConfigRules
{
    /// <summary>Ҳар навъи нод config-и typed-и худро дорад — ниг. Channels/Flows/FlowConfigs.cs.</summary>
    public static string? Validate(string type, JsonElement config)
    {
        try
        {
            switch (type.ToLowerInvariant())
            {
                case "message":
                    var message = config.Deserialize<MessageNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    // "payment" дар намуди JSON қабул карда мешавад (мутобиқат бо спека), вале
                    // қасдан рад мешавад — спека худаш "маҳсулоти пулакӣ"-ро дар НАГИР дорад.
                    if (message.Buttons.Any(b => b.Action == "payment"))
                        return "Тугмаи навъи 'payment' дастгирӣ намешавад.";
                    if (message.Buttons.Any(b => b.Action != MessageButton.ActionNext && b.Action != MessageButton.ActionUrl))
                        return "action-и тугма бояд 'next' ё 'url' бошад.";
                    if (message.Blocks.Any(b => (b.Text?.Length ?? 0) > MessageBlock.MaxTextLength
                        || (b.Variants ?? []).Any(v => (v?.Length ?? 0) > MessageBlock.MaxTextLength)))
                        return $"Матни паём то {MessageBlock.MaxTextLength} аломат бошад.";
                    if (message.Blocks.Any(b => (b.Variants?.Length ?? 0) > MessageBlock.MaxVariants))
                        return $"Паём то {MessageBlock.MaxVariants + 1} варианти матн дошта метавонад.";
                    if (message.Blocks.Any(b => b.Type != MessageBlock.TypeText && b.Variants is { Length: > 0 }))
                        return "Вариантҳо танҳо барои матн ҳастанд.";
                    break;
                case "condition":
                    _ = config.Deserialize<ConditionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                case "action":
                    var action = config.Deserialize<ActionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    if (string.IsNullOrEmpty(action.Kind))
                        return "kind лозим аст.";
                    break;
                case "note":
                    _ = config.Deserialize<NoteNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                default:
                    return $"Навъи нодуруст: {type}.";
            }
            return null;
        }
        catch (JsonException ex)
        {
            return $"config хонда нашуд: {ex.Message}";
        }
    }
}
