namespace Office.Api.Data.Entities;

/// <summary>
/// A person reached an automation's goal — the flow's "Конверсия" step. Once per person per
/// flow (the key), however many times they pass it or by whichever branch — the same rule as
/// ChatPlace's «Записать конверсию». Only FlowEngine writes it, for its own session's flow and
/// contact.
/// </summary>
public class FlowConversion
{
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;

    public Guid ContactId { get; set; }
    public Conversation Contact { get; set; } = null!;

    /// <summary>The session and step that got there first — for looking into it, not a link that must stay valid.</summary>
    public Guid? SessionId { get; set; }
    public Guid? NodeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
