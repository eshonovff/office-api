using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class ConversationAssignmentEventConfiguration : IEntityTypeConfiguration<ConversationAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<ConversationAssignmentEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.FromUserName).HasMaxLength(200);
        builder.Property(e => e.ToUserName).HasMaxLength(200);

        builder.HasOne(e => e.Conversation)
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ҳарду FK SetNull — номҳо snapshot шудаанд, пас нест кардани корбар набояд
        // ҳамчун Restrict садди нест карданро эҷод кунад ва набояд таърихро ҳам вайрон кунад.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.FromUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });
    }
}
