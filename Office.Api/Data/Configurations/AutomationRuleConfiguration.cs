using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.TriggerType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.TriggerConfigJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ConditionConfigJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ActionConfigJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne(r => r.Channel).WithMany().HasForeignKey(r => r.ChannelId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ChannelId);
    }
}
