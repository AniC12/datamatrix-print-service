using CodePrintManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class ProductNodeConfiguration : IEntityTypeConfiguration<ProductNode>
{
    public void Configure(EntityTypeBuilder<ProductNode> builder)
    {
        builder.ToTable("product_nodes");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.IsLeaf).HasDefaultValue(false);

        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
