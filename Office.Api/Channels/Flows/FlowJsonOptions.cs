using System.Text.Json;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Config-и ҳар нод (MessageNodeConfig/ConditionNodeConfig/ActionNodeConfig/NoteNodeConfig)
/// ҳамчун JsonElement-и хом, БЕ табдил, бевосита ба frontend мерасад (ниг. FlowNodeDto.Config) —
/// баръакси AutomationTriggerConfig (типи қавӣ), ки ҳамеша аз нав тавассути DTO-и типдор
/// serialize мешавад ва ASP.NET-и camelCase-и худкор барояш кофист.
///
/// Пас ҳар ҷое ки ин 4 конфигуратсия serialize/deserialize мешаванд (FlowTemplateSeeder,
/// FlowsEndpoints.ValidateNodeConfig, FlowEngine) бояд ин як JsonSerializerOptions-ро истифода
/// баранд — вагарна JsonSerializer.Serialize-и пешфарз (PascalCase) config_json месозад, ки
/// frontend-и camelCase намефаҳмад (config.rules → undefined, ниг. хатогии production 2026-09-15).
/// CaseInsensitive (қисми JsonSerializerDefaults.Web) config_json-и кӯҳнаи PascalCase (пеш аз ин
/// ислоҳ сохташуда)-ро низ дуруст мехонад.
/// </summary>
public static class FlowJsonOptions
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
