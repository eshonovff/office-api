using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class FlowConfiguration : IEntityTypeConfiguration<Flow>
{
    public void Configure(EntityTypeBuilder<Flow> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.TriggerType).HasMaxLength(50).IsRequired();
        builder.Property(f => f.TriggerConfigJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne(f => f.Channel).WithMany().HasForeignKey(f => f.ChannelId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.ChannelId);
    }
}

public class FlowNodeConfiguration : IEntityTypeConfiguration<FlowNode>
{
    public void Configure(EntityTypeBuilder<FlowNode> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.ConfigJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne(n => n.Flow).WithMany(f => f.Nodes).HasForeignKey(n => n.FlowId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => n.FlowId);
    }
}

public class FlowEdgeConfiguration : IEntityTypeConfiguration<FlowEdge>
{
    public void Configure(EntityTypeBuilder<FlowEdge> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromPort).HasMaxLength(50).IsRequired();

        builder.HasOne(e => e.Flow).WithMany(f => f.Edges).HasForeignKey(e => e.FlowId).OnDelete(DeleteBehavior.Cascade);

        // FromNodeId/ToNodeId қасдан FK-и EF надоранд (на navigation property) — canvas
        // node-ҳоро мустақил (батч) сабт мекунад, пеш аз он ки edge-ҳо санҷида шаванд; FK-и
        // сахт тартиби сабтро маҳдуд мекард. Ягонагии граф дар FlowsEndpoints санҷида мешавад.
        builder.HasIndex(e => new { e.FlowId, e.FromNodeId });
    }
}
