using Erp.Database;
using Erp.Treasury.Application;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Treasury.Infrastructure.Persistence;

/// <summary>
/// نگاشت به جدول‌های schema trs (ساختار فقط با اسکریپت‌های پوشه database).
/// روی اتصال مشترک شرکت کار می‌کند تا فرم و سند حسابداری‌اش در یک تراکنش ثبت شوند.
/// </summary>
public class TreasuryDbContext : DbContext, ITreasuryDbContext
{
    public TreasuryDbContext(DbContextOptions<TreasuryDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<CashBox> CashBoxes => Set<CashBox>();
    public IQueryable<BankAccount> BankAccounts => Set<BankAccount>();
    public IQueryable<ImprestFund> ImprestFunds => Set<ImprestFund>();
    public IQueryable<TransactionType> TransactionTypes => Set<TransactionType>();
    public IQueryable<Receipt> Receipts => Set<Receipt>();
    public IQueryable<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public IQueryable<ReceivedCheque> ReceivedCheques => Set<ReceivedCheque>();
    public IQueryable<Payment> Payments => Set<Payment>();
    public IQueryable<PaymentLine> PaymentLines => Set<PaymentLine>();
    public IQueryable<IssuedCheque> IssuedCheques => Set<IssuedCheque>();
    public IQueryable<Transfer> Transfers => Set<Transfer>();
    public IQueryable<ImprestSettlement> ImprestSettlements => Set<ImprestSettlement>();
    public IQueryable<ChequeOperation> ChequeOperations => Set<ChequeOperation>();
    public IQueryable<ChequeOperationLine> ChequeOperationLines => Set<ChequeOperationLine>();
    public IQueryable<Guarantee> Guarantees => Set<Guarantee>();
    public IQueryable<InstrumentBook> InstrumentBooks => Set<InstrumentBook>();
    public IQueryable<InstrumentLeaf> InstrumentLeaves => Set<InstrumentLeaf>();
    public IQueryable<BankGuarantee> BankGuarantees => Set<BankGuarantee>();
    public IQueryable<PersonRef> Persons => Set<PersonRef>().AsNoTracking();
    public IQueryable<BankRef> Banks => Set<BankRef>().AsNoTracking();
    public IQueryable<AccountRef> Accounts => Set<AccountRef>().AsNoTracking();
    public IQueryable<FactorRef> Factors => Set<FactorRef>().AsNoTracking();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();

    void ITreasuryDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void ITreasuryDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> ITreasuryDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TreasuryDbContext).Assembly);
}

internal static class Doc
{
    public static void Base<T>(EntityTypeBuilder<T> b, string table) where T : TreasuryDocument
    {
        b.ToTable(table, "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
        b.Property(x => x.RowVer).IsRowVersion();
        b.Ignore(x => x.DomainEvents);
    }

    public static PropertyBuilder<decimal> Money<T>(this EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, decimal>> p)
        where T : class => b.Property(p).HasPrecision(24, 4);
}

internal class CashBoxConfiguration : IEntityTypeConfiguration<CashBox>
{
    public void Configure(EntityTypeBuilder<CashBox> b)
    {
        b.ToTable("CashBoxes", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

internal class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("BankAccounts", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(150);
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.AccountNo).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.Iban).HasMaxLength(26).IsUnicode(false);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

internal class ImprestFundConfiguration : IEntityTypeConfiguration<ImprestFund>
{
    public void Configure(EntityTypeBuilder<ImprestFund> b)
    {
        b.ToTable("ImprestFunds", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

internal class TransactionTypeConfiguration : IEntityTypeConfiguration<TransactionType>
{
    public void Configure(EntityTypeBuilder<TransactionType> b)
    {
        b.ToTable("TransactionTypes", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100);
    }
}

internal class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        Doc.Base(b, "Receipts");
        b.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
    }
}

internal class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> b)
    {
        b.ToTable("ReceiptLines", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Property(x => x.RefNo).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.ReceiptId, x.RowNo }).IsUnique();
        b.HasOne(x => x.Cheque).WithOne().HasForeignKey<ReceivedCheque>(c => c.ReceiptLineId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ReceivedChequeConfiguration : IEntityTypeConfiguration<ReceivedCheque>
{
    public void Configure(EntityTypeBuilder<ReceivedCheque> b)
    {
        b.ToTable("ReceivedCheques", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Property(x => x.SerialNo).HasMaxLength(30);
        b.Property(x => x.SayadId).HasMaxLength(16).IsFixedLength().IsUnicode(false);
        b.Property(x => x.AccountNo).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.DrawerName).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(500);
    }
}

internal class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        Doc.Base(b, "Payments");
        b.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PaymentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
    }
}

internal class PaymentLineConfiguration : IEntityTypeConfiguration<PaymentLine>
{
    public void Configure(EntityTypeBuilder<PaymentLine> b)
    {
        b.ToTable("PaymentLines", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Property(x => x.RefNo).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.PaymentId, x.RowNo }).IsUnique();
        b.HasOne(x => x.Cheque).WithOne().HasForeignKey<IssuedCheque>(c => c.PaymentLineId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class IssuedChequeConfiguration : IEntityTypeConfiguration<IssuedCheque>
{
    public void Configure(EntityTypeBuilder<IssuedCheque> b)
    {
        b.ToTable("IssuedCheques", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Property(x => x.SerialNo).HasMaxLength(30);
        b.Property(x => x.SayadId).HasMaxLength(16).IsFixedLength().IsUnicode(false);
        b.Property(x => x.Description).HasMaxLength(500);
    }
}

internal class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> b)
    {
        Doc.Base(b, "Transfers");
        b.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");
        b.Money(x => x.Amount);
        b.Money(x => x.Fee);
        b.Property(x => x.RefNo).HasMaxLength(50);
    }
}

internal class ImprestSettlementConfiguration : IEntityTypeConfiguration<ImprestSettlement>
{
    public void Configure(EntityTypeBuilder<ImprestSettlement> b)
    {
        Doc.Base(b, "ImprestSettlements");
        b.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");
        b.Money(x => x.VatAmount);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SettlementId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.LinesTotal);
        b.Ignore(x => x.Total);
    }
}

internal class ImprestSettlementLineConfiguration : IEntityTypeConfiguration<ImprestSettlementLine>
{
    public void Configure(EntityTypeBuilder<ImprestSettlementLine> b)
    {
        b.ToTable("ImprestSettlementLines", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Money(x => x.VatAmount);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.SettlementId, x.RowNo }).IsUnique();
    }
}

internal class ChequeOperationConfiguration : IEntityTypeConfiguration<ChequeOperation>
{
    public void Configure(EntityTypeBuilder<ChequeOperation> b)
    {
        Doc.Base(b, "ChequeOperations");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OperationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DocType);
    }
}

internal class ChequeOperationLineConfiguration : IEntityTypeConfiguration<ChequeOperationLine>
{
    public void Configure(EntityTypeBuilder<ChequeOperationLine> b)
    {
        b.ToTable("ChequeOperationLines", "trs");
        b.HasKey(x => x.Id);
    }
}

internal class GuaranteeConfiguration : IEntityTypeConfiguration<Guarantee>
{
    public void Configure(EntityTypeBuilder<Guarantee> b)
    {
        Doc.Base(b, "Guarantees");
        b.Money(x => x.Amount);
        b.Property(x => x.Number).HasMaxLength(50);
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.IsReturned).HasColumnName("Status")
            .HasConversion(v => v ? (byte)2 : (byte)1, v => v == 2);
    }
}

internal class InstrumentBookConfiguration : IEntityTypeConfiguration<InstrumentBook>
{
    public void Configure(EntityTypeBuilder<InstrumentBook> b)
    {
        b.ToTable("InstrumentBooks", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Series).HasMaxLength(20);
        b.Property(x => x.MaxAmount).HasPrecision(24, 4);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasMany(x => x.Leaves).WithOne().HasForeignKey(l => l.BookId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Leaves).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal class InstrumentLeafConfiguration : IEntityTypeConfiguration<InstrumentLeaf>
{
    public void Configure(EntityTypeBuilder<InstrumentLeaf> b)
    {
        b.ToTable("InstrumentLeaves", "trs");
        b.HasKey(x => x.Id);
        b.Property(x => x.SerialNo).HasMaxLength(30);
        b.Property(x => x.VoidReason).HasMaxLength(200);
    }
}

internal class BankGuaranteeConfiguration : IEntityTypeConfiguration<BankGuarantee>
{
    public void Configure(EntityTypeBuilder<BankGuarantee> b)
    {
        b.ToTable("BankGuarantees", "trs");
        b.HasKey(x => x.Id);
        b.Money(x => x.Amount);
        b.Property(x => x.Number).HasMaxLength(50);
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

internal class PersonRefConfiguration : IEntityTypeConfiguration<PersonRef>
{
    public void Configure(EntityTypeBuilder<PersonRef> b)
    {
        b.ToTable("Persons", "base", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
    }
}

internal class BankRefConfiguration : IEntityTypeConfiguration<BankRef>
{
    public void Configure(EntityTypeBuilder<BankRef> b)
    {
        b.ToTable("Banks", "base", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
    }
}

internal class AccountRefConfiguration : IEntityTypeConfiguration<AccountRef>
{
    public void Configure(EntityTypeBuilder<AccountRef> b)
    {
        b.ToTable("Accounts", "fin", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
    }
}

internal class FactorRefConfiguration : IEntityTypeConfiguration<FactorRef>
{
    public void Configure(EntityTypeBuilder<FactorRef> b)
    {
        b.ToTable("CostFactors", "cost", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
    }
}

internal class CenterRefConfiguration : IEntityTypeConfiguration<CenterRef>
{
    public void Configure(EntityTypeBuilder<CenterRef> b)
    {
        b.HasKey(c => c.Id);
        b.ToSqlQuery("SELECT c.Id, c.Title, t.[Function] AS [Function], c.IsActive " +
                     "FROM cost.CostCenters c JOIN cost.CostCenterTypes t ON t.Id = c.CenterTypeId");
    }
}
