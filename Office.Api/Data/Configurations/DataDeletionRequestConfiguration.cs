using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class DataDeletionRequestConfiguration : IEntityTypeConfiguration<DataDeletionRequest>
{
    public void Configure(EntityTypeBuilder<DataDeletionRequest> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Provider).HasMaxLength(20);
        builder.Property(r => r.MetaUserId).HasMaxLength(100);
        builder.Property(r => r.ConfirmationCode).HasMaxLength(32);
        builder.Property(r => r.SignedRequestHash).HasMaxLength(64);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.ConfirmationCode).IsUnique();
        builder.HasIndex(r => r.SignedRequestHash).IsUnique();
    }
}
