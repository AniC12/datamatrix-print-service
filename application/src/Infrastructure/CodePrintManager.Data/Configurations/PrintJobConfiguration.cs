using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("print_jobs");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasDefaultValue(JobStatus.Preparing);
        builder.Property(e => e.CodesConfirmed).HasDefaultValue(0);

        builder.HasOne(e => e.Product)
            .WithMany(e => e.PrintJobs)
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Printer)
            .WithMany(e => e.PrintJobs)
            .HasForeignKey(e => e.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.Status);

        // Concurrency guard: at most one active job per printer
        builder.HasIndex(e => e.PrinterId)
            .HasFilter("[Status] IN ('Preparing', 'Ready', 'Printing')")
            .IsUnique();

        // Concurrency guard: at most one active job per product
        builder.HasIndex(e => e.ProductId)
            .HasFilter("[Status] IN ('Preparing', 'Ready', 'Printing')")
            .IsUnique();
    }
}
