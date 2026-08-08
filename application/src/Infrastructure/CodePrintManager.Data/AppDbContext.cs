using CodePrintManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<ProductNode> ProductNodes => Set<ProductNode>();
    public DbSet<Code> Codes => Set<Code>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
