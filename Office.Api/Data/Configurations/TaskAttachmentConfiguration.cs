using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Office.Api.Data.Entities;

namespace Office.Api.Data.Configurations;

public class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).HasMaxLength(300).IsRequired();
        builder.Property(a => a.FilePath).HasMaxLength(1000).IsRequired();

        builder.HasOne(a => a.Task)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Uploader)
            .WithMany()
            .HasForeignKey(a => a.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
