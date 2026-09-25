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
        builder.Property(c => c.WebhookSetupWarning).HasMaxLength(500);

        // Globally unique, not per owner: one Instagram account can belong to exactly one owner
        // (the company or one мизоҷ) — connecting it elsewhere must be refused, not re-owned.
        builder.HasIndex(c => new { c.Type, c.ExternalId }).IsUnique();

        builder.HasIndex(c => c.CustomerId);
        builder.Property(c => c.MetaAppScopedUserId).HasMaxLength(100);
        builder.HasIndex(c => c.MetaAppScopedUserId);
        // Restrict, like Conversation → Channel: removing a мизоҷ's data is an explicit
        // operation (Meta data-deletion callback), never a side effect of a cascade.
        builder.HasOne(c => c.Customer)
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
