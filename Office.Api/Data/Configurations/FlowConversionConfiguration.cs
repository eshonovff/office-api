using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class FlowConversionConfiguration : IEntityTypeConfiguration<FlowConversion>
{
    public void Configure(EntityTypeBuilder<FlowConversion> builder)
    {
        builder.ToTable("flow_conversions");
        builder.HasKey(c => new { c.FlowId, c.ContactId }); // once per person per flow

        builder.HasOne(c => c.Flow).WithMany().HasForeignKey(c => c.FlowId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Contact).WithMany().HasForeignKey(c => c.ContactId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.FlowId, c.CreatedAt });
        builder.HasIndex(c => c.ContactId);
    }
}
