using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class ContactTagConfiguration : IEntityTypeConfiguration<ContactTag>
{
    public void Configure(EntityTypeBuilder<ContactTag> builder)
    {
        builder.HasKey(t => new { t.ContactId, t.Tag });

        builder.Property(t => t.Tag).HasMaxLength(100);

        builder.HasOne(t => t.Contact).WithMany().HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ContactVariableConfiguration : IEntityTypeConfiguration<ContactVariable>
{
    public void Configure(EntityTypeBuilder<ContactVariable> builder)
    {
        builder.HasKey(v => new { v.ContactId, v.Key });

        builder.Property(v => v.Key).HasMaxLength(100);
        builder.Property(v => v.Value).HasMaxLength(2000);

        builder.HasOne(v => v.Contact).WithMany().HasForeignKey(v => v.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}
