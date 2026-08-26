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
        // МУҲИМ (2026-08-25, ниг. report): пеш аз ин 200 буд — барои media id-и кӯтоҳи WhatsApp
        // кофӣ, вале Facebook/Instagram дар ин майдон URL-и пурраи CDN-и имзошударо (бо
        // signature-и дароз, аксар вақт 300-600+ ҳарф) захира мекунанд. Натиҷа: HAR як боркунии
        // муваффақи FB/IG дар SaveChangesAsync бо PostgresException 22001 "value too long"
        // мешикаст — файл ба диск НАВИШТА МЕШУД, вале сабти DB ҳеҷ гоҳ намерасид, ва паём
        // абадан "боркунӣ..." мемонд, ҳатто баъд аз 5 кӯшиши AutomaticRetry. Ҳудуд бардошта шуд.
        builder.Property(m => m.MediaExternalId);
        builder.Property(m => m.SentByUserName).HasMaxLength(200);
        builder.Property(m => m.ThumbnailUrl).HasMaxLength(500);
        // smallint[] (0-100), на jsonb-и float — 32-40 адад ба ҳар паём ҷамъ мешавад,
        // ин шакл нисбат ба jsonb хеле фишурдатар аст. DTO ба 0-1 табдил медиҳад.
        builder.Property(m => m.WaveformPeaks).HasColumnType("smallint[]");
        builder.Property(m => m.MediaDownloadError).HasMaxLength(1000);
        builder.Property(m => m.TemplateName).HasMaxLength(200);
        builder.Property(m => m.TemplateLanguage).HasMaxLength(20);
        builder.Property(m => m.TemplateParametersJson).HasColumnType("jsonb");
        builder.Property(m => m.FailureReason).HasMaxLength(500);
        builder.Property(m => m.FailureDetail).HasMaxLength(4000);

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
