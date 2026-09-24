using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class SubscriptionRequestConfiguration : IEntityTypeConfiguration<SubscriptionRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionRequest> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Tier).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ExpectedAmount).HasPrecision(12, 2);
        builder.Property(r => r.ReceiptPath).HasMaxLength(500);
        builder.Property(r => r.ReceiptFileName).HasMaxLength(300);
        builder.Property(r => r.PaidToBank).HasMaxLength(100);
        builder.Property(r => r.PaidToCardNumber).HasMaxLength(30);
        builder.Property(r => r.ReviewedByUserName).HasMaxLength(200);
        builder.Property(r => r.ReviewNote).HasMaxLength(1000);

        builder.HasIndex(r => new { r.CustomerId, r.Status });
        builder.HasIndex(r => new { r.Status, r.SubmittedAt });

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reviewer name is snapshotted, so deleting the staff user must not block or erase history.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ReviewedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
