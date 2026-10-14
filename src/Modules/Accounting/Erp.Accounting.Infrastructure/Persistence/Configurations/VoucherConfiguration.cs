using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> b)
    {
        b.ToTable("Vouchers", "fin", t =>
        {
            t.HasTrigger("trg_Vouchers_Validate");
            t.HasTrigger("trg_Vouchers_Protect");
        });
        b.HasKey(v => v.Id);
        b.Property(v => v.Type).HasColumnName("VoucherType");
        b.Property(v => v.Description).HasMaxLength(500);
        b.Property(v => v.SourceModule).HasMaxLength(30).IsUnicode(false);
        b.Property(v => v.SourceDocType).HasMaxLength(30).IsUnicode(false);
        b.Property(v => v.CreatedAt).HasColumnType("datetime2(0)");
        b.Property(v => v.ApprovedAt).HasColumnType("datetime2(0)");
        b.Property(v => v.RowVer).IsRowVersion();
        b.HasOne<FiscalYear>().WithMany().HasForeignKey(v => v.FiscalYearId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(v => v.Lines).WithOne().HasForeignKey(l => l.VoucherId).OnDelete(DeleteBehavior.ClientCascade);
        b.Navigation(v => v.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(v => v.DomainEvents);
        b.Ignore(v => v.TotalDebit);
        b.Ignore(v => v.TotalCredit);
        b.Ignore(v => v.IsBalanced);
        b.Ignore(v => v.IsSystemGenerated);
    }
}

internal class VoucherLineConfiguration : IEntityTypeConfiguration<VoucherLine>
{
    public void Configure(EntityTypeBuilder<VoucherLine> b)
    {
        b.ToTable("VoucherLines", "fin", t => t.HasTrigger("trg_VoucherLines_Validate"));
        b.HasKey(l => l.Id);
        b.Property(l => l.Debit).HasPrecision(24, 4);
        b.Property(l => l.Credit).HasPrecision(24, 4);
        b.Property(l => l.CurrencyAmount).HasPrecision(24, 4);
        b.Property(l => l.ExchangeRate).HasPrecision(19, 6);
        b.Property(l => l.Quantity).HasPrecision(19, 4);
        b.Property(l => l.Description).HasMaxLength(500);
        b.Property(l => l.FollowUpNo).HasMaxLength(50);
        b.Ignore(l => l.DetailIds);

        b.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Currency>().WithMany().HasForeignKey(l => l.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(l => l.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail1Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail2Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail3Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail4Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail5Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DetailAccount>().WithMany().HasForeignKey(l => l.Detail6Id).OnDelete(DeleteBehavior.Restrict);
    }
}
