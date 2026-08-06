namespace Office.Api.Data.Entities;

public class ChannelMember
{
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
