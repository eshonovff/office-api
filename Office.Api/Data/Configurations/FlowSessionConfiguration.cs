using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class FlowSessionConfiguration : IEntityTypeConfiguration<FlowSession>
{
    public void Configure(EntityTypeBuilder<FlowSession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TriggerExternalId).HasMaxLength(100);
        builder.Property(s => s.VariablesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.WaitReason).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ScheduledJobId).HasMaxLength(50);

        builder.HasOne(s => s.Flow).WithMany().HasForeignKey(s => s.FlowId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Contact).WithMany().HasForeignKey(s => s.ContactId).OnDelete(DeleteBehavior.Cascade);

        // FlowTriggerProcessor: "оё ин contact аллакай сессияи фаъол/интизор дар ин flow дорад?"
        builder.HasIndex(s => new { s.FlowId, s.ContactId, s.Status });
        // FlowTriggerProcessor: идемпотентӣ — "ин comment_id/message_id аллакай сессия сохт?"
        builder.HasIndex(s => new { s.FlowId, s.TriggerExternalId });
        // Analytics: who started a flow in a period.
        builder.HasIndex(s => new { s.FlowId, s.CreatedAt });
        // FlowEngineJob-и таъхир: "кадом сессияҳо бояд бедор шаванд?" (сканкунии recurring, агар лозим шавад).
        builder.HasIndex(s => s.ResumeAt);
    }
}

public class FlowSessionStepConfiguration : IEntityTypeConfiguration<FlowSessionStep>
{
    public void Configure(EntityTypeBuilder<FlowSessionStep> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FromPort).HasMaxLength(50);

        builder.HasOne(s => s.Session).WithMany().HasForeignKey(s => s.SessionId).OnDelete(DeleteBehavior.Cascade);

        // GET /api/flows/{id}/stats: GROUP BY node_id аз рӯи session-ҳои ин flow.
        builder.HasIndex(s => s.NodeId);
    }
}
