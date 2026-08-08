using CodePrintManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> builder)
    {
        builder.ToTable("printers");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.IpAddress).IsRequired();
        builder.Property(e => e.Port).HasDefaultValue(9100);
        builder.Property(e => e.AdapterType).HasDefaultValue("savema_tto");
        builder.Property(e => e.IsActive).HasDefaultValue(true);
    }
}
