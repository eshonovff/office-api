using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(30);
        builder.Property(u => u.PermissionsVersion).HasDefaultValue(1);
        builder.Property(u => u.IsActive).HasDefaultValue(true);

        builder.HasIndex(u => u.Username).IsUnique();
    }
}
