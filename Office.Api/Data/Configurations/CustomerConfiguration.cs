using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Email).HasMaxLength(200).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.AvatarUrl).HasMaxLength(500);
        builder.Property(c => c.EmailVerificationCodeHash).HasMaxLength(64);
        builder.Property(c => c.IsActive).HasDefaultValue(true);
        builder.Property(c => c.PlanTier).HasConversion<string>().HasMaxLength(20);

        // Email пеш аз захира ба lowercase меояд (CustomerAuthEndpoints.NormalizeEmail) — пас
        // unique index-и оддӣ кофист, citext ё functional index лозим нест.
        builder.HasIndex(c => c.Email).IsUnique();
    }
}
