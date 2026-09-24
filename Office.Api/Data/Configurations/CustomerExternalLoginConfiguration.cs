using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class CustomerExternalLoginConfiguration : IEntityTypeConfiguration<CustomerExternalLogin>
{
    public void Configure(EntityTypeBuilder<CustomerExternalLogin> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.ProviderUserId).HasMaxLength(200).IsRequired();

        // Як "sub" — як мизоз. (Provider, ProviderUserId) якҷоя, чун Google-и sub=123 ва
        // Apple-и sub=123 ду корбари гуногунанд.
        builder.HasIndex(l => new { l.Provider, l.ProviderUserId }).IsUnique();
        builder.HasIndex(l => l.CustomerId);

        builder.HasOne(l => l.Customer)
            .WithMany(c => c.ExternalLogins)
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
