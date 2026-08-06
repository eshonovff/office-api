namespace Office.Api.Data.Entities;

public class Channel
{
    public Guid Id { get; set; }
    public ChannelType Type { get; set; }
    public required string Name { get; set; }
    public required string ExternalId { get; set; }
    public string? CredentialsEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ChannelMember> Members { get; set; } = [];
    public ICollection<Conversation> Conversations { get; set; } = [];
}
