using Erp.Accounting.Domain.BaseData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("Persons", "base");
        b.HasKey(p => p.Id);
        b.Property(p => p.Kind).HasColumnName("PersonKind");
        b.Property(p => p.DisplayName).HasMaxLength(200);
        b.Property(p => p.NationalId).HasMaxLength(12).IsUnicode(false);
        b.Property(p => p.EconomicCode).HasMaxLength(20).IsUnicode(false);
        b.Ignore(p => p.DomainEvents);
    }
}

internal class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> b)
    {
        b.ToTable("Currencies", "base");
        b.HasKey(c => c.Id);
        b.Property(c => c.Code).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(c => c.Title).HasMaxLength(50);
    }
}

internal class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.ToTable("Units", "base");
        b.HasKey(u => u.Id);
        b.Property(u => u.Title).HasMaxLength(50);
    }
}
