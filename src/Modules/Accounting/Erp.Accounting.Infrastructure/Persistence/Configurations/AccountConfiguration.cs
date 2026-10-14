using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts", "fin", t => t.HasTrigger("trg_Accounts_ParentLevel"));
        b.HasKey(a => a.Id);
        b.Property(a => a.Code).HasMaxLength(20).IsUnicode(false);
        b.Property(a => a.Title).HasMaxLength(200);
        b.Property(a => a.Type).HasColumnName("AccountType");
        b.HasOne<Account>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(a => a.DetailLevels).WithOne().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(a => a.DetailLevels).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(a => a.DomainEvents);
    }
}

internal class AccountDetailLevelConfiguration : IEntityTypeConfiguration<AccountDetailLevel>
{
    public void Configure(EntityTypeBuilder<AccountDetailLevel> b)
    {
        b.ToTable("AccountDetailLevels", "fin");
        b.HasKey(l => new { l.AccountId, l.LevelNo });
        b.HasMany(l => l.AllowedTypes).WithOne()
            .HasForeignKey(t => new { t.AccountId, t.LevelNo }).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.AllowedTypes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(l => l.AllowedRoles).WithOne()
            .HasForeignKey(r => new { r.AccountId, r.LevelNo }).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.AllowedRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal class AccountDetailLevelTypeConfiguration : IEntityTypeConfiguration<AccountDetailLevelType>
{
    public void Configure(EntityTypeBuilder<AccountDetailLevelType> b)
    {
        b.ToTable("AccountDetailLevelTypes", "fin");
        b.HasKey(t => new { t.AccountId, t.LevelNo, t.DetailTypeId });
        b.HasOne<DetailType>().WithMany().HasForeignKey(t => t.DetailTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class AccountDetailLevelRoleConfiguration : IEntityTypeConfiguration<AccountDetailLevelRole>
{
    public void Configure(EntityTypeBuilder<AccountDetailLevelRole> b)
    {
        b.ToTable("AccountDetailLevelRoles", "fin");
        b.HasKey(r => new { r.AccountId, r.LevelNo, r.RoleId });
        b.HasOne<PersonRoleType>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
