using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class CustomerRefreshTokenConfiguration : IEntityTypeConfiguration<CustomerRefreshToken>
{
    public void Configure(EntityTypeBuilder<CustomerRefreshToken> builder)
    {
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(rt => rt.CreatedByIp).HasMaxLength(64);

        builder.HasIndex(rt => rt.TokenHash).IsUnique();
        builder.HasIndex(rt => rt.CustomerId);

        builder.HasOne(rt => rt.Customer)
            .WithMany(c => c.RefreshTokens)
            .HasForeignKey(rt => rt.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
