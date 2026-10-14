using Erp.Costing.Application.Abstractions;
using Erp.Costing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Costing.Infrastructure.Persistence.Configurations;

internal class CostFactorConfiguration : IEntityTypeConfiguration<CostFactor>
{
    public void Configure(EntityTypeBuilder<CostFactor> b)
    {
        b.ToTable("CostFactors", "cost");
        b.HasKey(f => f.Id);
        b.Property(f => f.Title).HasMaxLength(200);
        b.Property(f => f.VariablePercent).HasPrecision(5, 2);
        b.Property(f => f.Notes).HasMaxLength(500);
        b.Ignore(f => f.DomainEvents);
    }
}

internal class CostCenterTypeConfiguration : IEntityTypeConfiguration<CostCenterType>
{
    public void Configure(EntityTypeBuilder<CostCenterType> b)
    {
        b.ToTable("CostCenterTypes", "cost");
        b.HasKey(t => t.Id);
        b.Property(t => t.Title).HasMaxLength(100);
    }
}

internal class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> b)
    {
        b.ToTable("CostCenters", "cost");
        b.HasKey(c => c.Id);
        b.Property(c => c.Title).HasMaxLength(200);
        b.Property(c => c.Notes).HasMaxLength(500);
        b.HasOne<CostCenterType>().WithMany().HasForeignKey(c => c.CenterTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CostCenter>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Allocations).WithOne().HasForeignKey(a => a.CenterId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(c => c.DomainEvents);
    }
}

internal class CostCenterAllocationConfiguration : IEntityTypeConfiguration<CostCenterAllocation>
{
    public void Configure(EntityTypeBuilder<CostCenterAllocation> b)
    {
        b.ToTable("CostCenterAllocations", "cost");
        b.HasKey(a => new { a.CenterId, a.TargetCenterId });
        b.Property(a => a.Percent).HasPrecision(5, 2);
        b.HasOne<CostCenter>().WithMany().HasForeignKey(a => a.TargetCenterId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>فقط‌خواندنی از schema base.</summary>
internal class LookupItemRefConfiguration : IEntityTypeConfiguration<LookupItemRef>
{
    public void Configure(EntityTypeBuilder<LookupItemRef> b)
    {
        b.HasKey(i => i.Id);
        b.ToSqlQuery("SELECT i.Id, c.Code AS CategoryCode, i.Title, i.SortOrder, i.IsActive " +
                     "FROM base.LookupItems i JOIN base.LookupCategories c ON c.Id = i.CategoryId");
    }
}

internal class PersonRefConfiguration : IEntityTypeConfiguration<PersonRef>
{
    public void Configure(EntityTypeBuilder<PersonRef> b)
    {
        b.ToTable("Persons", "base", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);
    }
}
