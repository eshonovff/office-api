using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class InstagramCommentConfiguration : IEntityTypeConfiguration<InstagramComment>
{
    public void Configure(EntityTypeBuilder<InstagramComment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(c => c.MediaExternalId).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ParentExternalId).HasMaxLength(100);
        builder.Property(c => c.AuthorExternalId).HasMaxLength(100).IsRequired();
        builder.Property(c => c.AuthorUsername).HasMaxLength(100);
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.AutoReplyError).HasMaxLength(500);

        // A webhook delivered twice, or a sync overlapping a webhook, must never make two rows.
        builder.HasIndex(c => new { c.ChannelId, c.ExternalId }).IsUnique();
        builder.HasIndex(c => new { c.ChannelId, c.MediaExternalId, c.CommentedAt });
        builder.HasIndex(c => new { c.ChannelId, c.IsRead });
        // Analytics: an account's comments in a period.
        builder.HasIndex(c => new { c.ChannelId, c.CommentedAt });

        builder.HasOne(c => c.Channel)
            .WithMany()
            .HasForeignKey(c => c.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
