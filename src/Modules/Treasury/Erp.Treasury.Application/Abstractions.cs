using Erp.Costing.Contracts;
using Erp.Treasury.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Treasury.Application;

public interface ITreasuryDbContext
{
    IQueryable<CashBox> CashBoxes { get; }
    IQueryable<BankAccount> BankAccounts { get; }
    IQueryable<ImprestFund> ImprestFunds { get; }
    IQueryable<TransactionType> TransactionTypes { get; }
    IQueryable<Receipt> Receipts { get; }
    IQueryable<ReceiptLine> ReceiptLines { get; }
    IQueryable<ReceivedCheque> ReceivedCheques { get; }
    IQueryable<Payment> Payments { get; }
    IQueryable<PaymentLine> PaymentLines { get; }
    IQueryable<IssuedCheque> IssuedCheques { get; }
    IQueryable<Transfer> Transfers { get; }
    IQueryable<ImprestSettlement> ImprestSettlements { get; }
    IQueryable<ChequeOperation> ChequeOperations { get; }
    IQueryable<ChequeOperationLine> ChequeOperationLines { get; }
    IQueryable<Guarantee> Guarantees { get; }

    /// <summary>فقط‌خواندنی: اشخاص (اطلاعات پایه).</summary>
    IQueryable<PersonRef> Persons { get; }
    /// <summary>فقط‌خواندنی: بانک‌ها (اطلاعات پایه).</summary>
    IQueryable<BankRef> Banks { get; }
    /// <summary>فقط‌خواندنی: حساب‌ها (حسابداری) برای معین انواع دریافت و پرداخت.</summary>
    IQueryable<AccountRef> Accounts { get; }
    /// <summary>فقط‌خواندنی: عوامل هزینه (بهای تمام‌شده).</summary>
    IQueryable<FactorRef> Factors { get; }
    /// <summary>فقط‌خواندنی: مراکز هزینه با وظیفه نوعشان.</summary>
    IQueryable<CenterRef> Centers { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class PersonRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class BankRef
{
    public short Id { get; init; }
    public string Title { get; init; } = "";
}

public sealed class AccountRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public byte Level { get; init; }
    public bool IsActive { get; init; }
}

public sealed class FactorRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public CostNature Nature { get; init; }
    public bool IsActive { get; init; }
}

public sealed class CenterRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public CostFunction Function { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>ثابت‌های ماژول.</summary>
public static class TreasuryModule
{
    public const string Name = "treasury";
}

public static class DependencyInjection
{
    public static IServiceCollection AddTreasuryApplication(this IServiceCollection services)
    {
        services.AddScoped<TreasurySetupService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<TransferService>();
        services.AddScoped<ImprestSettlementService>();
        services.AddScoped<ChequeOperationService>();
        services.AddScoped<GuaranteeService>();
        services.AddScoped<TreasuryReportService>();
        return services;
    }
}
