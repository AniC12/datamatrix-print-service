using CodePrintManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodePrintManager.Data.Configurations;

public class AppConfigConfiguration : IEntityTypeConfiguration<AppConfig>
{
    public void Configure(EntityTypeBuilder<AppConfig> builder)
    {
        builder.ToTable("app_config");

        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Value).IsRequired().HasMaxLength(500);
    }
}
