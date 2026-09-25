using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class FlowTemplateConfiguration : IEntityTypeConfiguration<FlowTemplate>
{
    public void Configure(EntityTypeBuilder<FlowTemplate> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.DefinitionJson).HasColumnType("jsonb").IsRequired();
    }
}
