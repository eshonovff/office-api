using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ContactName).HasMaxLength(200);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(c => c.Channel)
            .WithMany(ch => ch.Conversations)
            .HasForeignKey(c => c.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Assignee)
            .WithMany()
            .HasForeignKey(c => c.AssignedTo)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.ChannelId, c.ExternalId }).IsUnique();
        builder.HasIndex(c => new { c.ChannelId, c.Status, c.LastMessageAt });
        builder.HasIndex(c => c.AssignedTo).HasFilter("assigned_to IS NOT NULL");
    }
}
