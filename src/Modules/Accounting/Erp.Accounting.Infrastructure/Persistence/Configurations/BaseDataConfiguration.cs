using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.BaseData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

/// <summary>نمای فقط‌خواندنی جدول‌های ماژول اطلاعات پایه که حسابداری برای فیلتر لازم دارد.</summary>
internal class PersonRoleLinkConfiguration : IEntityTypeConfiguration<PersonRoleLink>
{
    public void Configure(EntityTypeBuilder<PersonRoleLink> b)
    {
        b.ToTable("PersonRoles", "base", t => t.ExcludeFromMigrations());
        b.HasKey(r => new { r.PersonId, r.RoleId });
    }
}

internal class RoleTypeRefConfiguration : IEntityTypeConfiguration<RoleTypeRef>
{
    public void Configure(EntityTypeBuilder<RoleTypeRef> b)
    {
        b.ToTable("PersonRoleTypes", "base", t => t.ExcludeFromMigrations());
        b.HasKey(r => r.Id);
        b.Property(r => r.Title).HasMaxLength(50);
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
