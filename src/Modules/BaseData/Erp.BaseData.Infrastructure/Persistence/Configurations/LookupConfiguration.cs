using Erp.BaseData.Domain.Banks;
using Erp.BaseData.Domain.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.BaseData.Infrastructure.Persistence.Configurations;

internal class LookupCategoryConfiguration : IEntityTypeConfiguration<LookupCategory>
{
    public void Configure(EntityTypeBuilder<LookupCategory> b)
    {
        b.ToTable("LookupCategories", "base");
        b.HasKey(c => c.Id);
        b.Property(c => c.Code).HasMaxLength(40).IsUnicode(false);
        b.Property(c => c.Title).HasMaxLength(100);
        b.Property(c => c.Description).HasMaxLength(300);
    }
}

internal class LookupItemConfiguration : IEntityTypeConfiguration<LookupItem>
{
    public void Configure(EntityTypeBuilder<LookupItem> b)
    {
        b.ToTable("LookupItems", "base", t => t.HasTrigger("trg_LookupItems_Parent"));
        b.HasKey(i => i.Id);
        b.Property(i => i.Code).HasMaxLength(20);
        b.Property(i => i.Title).HasMaxLength(150);
        b.HasOne<LookupCategory>().WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LookupItem>().WithMany().HasForeignKey(i => i.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> b)
    {
        b.ToTable("Banks", "base");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(60);
        b.Property(x => x.IbanCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.CardPrefixes).HasMaxLength(300).IsUnicode(false);
    }
}
