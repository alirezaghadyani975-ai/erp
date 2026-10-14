using Erp.Accounting.Domain.FiscalYears;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> b)
    {
        b.ToTable("FiscalYears", "fin");
        b.HasKey(f => f.Id);
        b.Property(f => f.Title).HasMaxLength(50);
        b.HasMany(f => f.Periods).WithOne().HasForeignKey(p => p.FiscalYearId);
        b.Navigation(f => f.Periods).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(f => f.DomainEvents);
    }
}

internal class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> b)
    {
        b.ToTable("FiscalPeriods", "fin");
        b.HasKey(p => p.Id);
        b.Property(p => p.Title).HasMaxLength(50);
    }
}
