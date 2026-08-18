using CodePrintManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class ArchivedCodeConfiguration : IEntityTypeConfiguration<ArchivedCode>
{
    public void Configure(EntityTypeBuilder<ArchivedCode> builder)
    {
        builder.ToTable("archived_codes");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.CodeText).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.ArchivedAt).IsRequired();

        builder.HasIndex(e => new { e.ProductId, e.ArchivedAt });
    }
}
