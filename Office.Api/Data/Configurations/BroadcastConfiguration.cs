using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.TagsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(b => b.Text).HasMaxLength(1000);
        builder.Property(b => b.MediaId).HasMaxLength(64);
        builder.Property(b => b.ButtonTitle).HasMaxLength(20);
        builder.Property(b => b.ButtonUrl).HasMaxLength(500);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Error).HasMaxLength(500);
        builder.Property(b => b.JobId).HasMaxLength(100);

        builder.HasIndex(b => new { b.ChannelId, b.CreatedAt });
        builder.HasIndex(b => new { b.ChannelId, b.Status });

        builder.HasOne(b => b.Channel)
            .WithMany()
            .HasForeignKey(b => b.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        // A deleted flow leaves the broadcast's history; a scheduled one then fails with a reason.
        builder.HasOne(b => b.Flow)
            .WithMany()
            .HasForeignKey(b => b.FlowId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class BroadcastRecipientConfiguration : IEntityTypeConfiguration<BroadcastRecipient>
{
    public void Configure(EntityTypeBuilder<BroadcastRecipient> builder)
    {
        builder.HasKey(r => new { r.BroadcastId, r.ContactId });

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Error).HasMaxLength(500);

        // "Did this person get a broadcast in the last 24 hours?" — asked for every recipient.
        builder.HasIndex(r => new { r.ContactId, r.SentAt });

        builder.HasOne(r => r.Broadcast)
            .WithMany(b => b.Recipients)
            .HasForeignKey(r => r.BroadcastId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Contact)
            .WithMany()
            .HasForeignKey(r => r.ContactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
