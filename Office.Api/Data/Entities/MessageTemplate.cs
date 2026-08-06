namespace Office.Api.Data.Entities;

public class MessageTemplate
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Shortcut { get; set; }
    public required string Body { get; set; }
    public ChannelType ChannelType { get; set; }

    public Guid CreatedBy { get; set; }
    public User Creator { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
