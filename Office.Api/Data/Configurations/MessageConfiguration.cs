using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.DeliveryStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ExternalId).HasMaxLength(200);
        builder.Property(m => m.MimeType).HasMaxLength(100);
        builder.Property(m => m.OriginalFileName).HasMaxLength(300);
        builder.Property(m => m.MediaExternalId).HasMaxLength(200);
        builder.Property(m => m.ThumbnailUrl).HasMaxLength(500);
        // smallint[] (0-100), на jsonb-и float — 32-40 адад ба ҳар паём ҷамъ мешавад,
        // ин шакл нисбат ба jsonb хеле фишурдатар аст. DTO ба 0-1 табдил медиҳад.
        builder.Property(m => m.WaveformPeaks).HasColumnType("smallint[]");
        builder.Property(m => m.MediaDownloadError).HasMaxLength(1000);

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.SentByUser)
            .WithMany()
            .HasForeignKey(m => m.SentByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        builder.HasIndex(m => m.ExternalId).IsUnique().HasFilter("external_id IS NOT NULL");
    }
}
