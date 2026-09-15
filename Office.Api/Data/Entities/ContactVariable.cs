namespace Office.Api.Data.Entities;

/// <summary>
/// Тағйирёбандаҳои contact (= Conversation) — аз action:set_variable/collect_input сабт
/// мешаванд, дар матни паём бо синтаксиси {{key}} истифода мешаванд (FlowVariableInterpolator).
/// Ин ду сарчашма аз FlowSession.VariablesJson (танҳо доираи ин сессия) фарқ мекунад: доимист,
/// байни flow-ҳо ва сессияҳо мубодила мешавад.
/// </summary>
public class ContactVariable
{
    public Guid ContactId { get; set; }
    public Conversation Contact { get; set; } = null!;
    public required string Key { get; set; }
    public required string Value { get; set; }
}
