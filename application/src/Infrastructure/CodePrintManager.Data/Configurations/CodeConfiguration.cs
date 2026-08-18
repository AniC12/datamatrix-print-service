using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class CodeConfiguration : IEntityTypeConfiguration<Code>
{
    public void Configure(EntityTypeBuilder<Code> builder)
    {
        builder.ToTable("codes");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.CodeText).IsRequired();
        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasDefaultValue(CodeStatus.Available);

        // Global uniqueness — a code can never exist twice in the system
        builder.HasIndex(e => e.CodeText).IsUnique();

        builder.HasIndex(e => new { e.ProductId, e.Status });
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Product)
            .WithMany(e => e.Codes)
            .HasForeignKey(e => e.ProductId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Job)
            .WithMany(e => e.Codes)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
