using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class WebhookLogConfiguration : IEntityTypeConfiguration<WebhookLog>
{
    public void Configure(EntityTypeBuilder<WebhookLog> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Provider).HasMaxLength(20).IsRequired();
        builder.Property(w => w.RawJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(w => w.ReceivedAt);
    }
}
