using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ExternalId).HasMaxLength(200).IsRequired();

        builder.HasIndex(c => new { c.Type, c.ExternalId }).IsUnique();
    }
}
