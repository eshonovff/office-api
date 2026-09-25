using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class CustomerPasswordResetConfiguration : IEntityTypeConfiguration<CustomerPasswordReset>
{
    public void Configure(EntityTypeBuilder<CustomerPasswordReset> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TokenHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => new { r.CustomerId, r.CreatedAt });

        builder.HasOne(r => r.Customer)
            .WithMany(c => c.PasswordResets)
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
