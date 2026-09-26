using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class AutomationRunConfiguration : IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<AutomationRun> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TriggerExternalId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ActorExternalId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TargetMediaExternalId).HasMaxLength(100);
        builder.Property(r => r.MatchedKeyword).HasMaxLength(200);
        builder.Property(r => r.CommentReplyStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DmStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FollowCheckResult).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(r => r.Rule).WithMany(rule => rule.Runs).HasForeignKey(r => r.RuleId).OnDelete(DeleteBehavior.Cascade);

        // Cooldown-и WebhookProcessor'ро истифода мебарад: "охирин run барои ин actor дар ин
        // пост, дар ин rule" — ниг. CommentAutomationProcessor.
        builder.HasIndex(r => new { r.RuleId, r.ActorExternalId, r.TargetMediaExternalId, r.CreatedAt });
        // Analytics: a rule's runs in a period.
        builder.HasIndex(r => new { r.RuleId, r.CreatedAt });
    }
}
