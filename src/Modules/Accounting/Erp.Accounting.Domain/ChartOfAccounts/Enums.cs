namespace Erp.Accounting.Domain.ChartOfAccounts;

/// <summary>سطح حساب (fin.Accounts.Level).</summary>
public enum AccountLevel : byte
{
    Group = 1,      // گروه
    General = 2,    // کل
    Subsidiary = 3  // معین
}

/// <summary>ماهیت حساب (fin.Accounts.Nature).</summary>
public enum AccountNature : byte
{
    Debit = 1,   // بدهکار
    Credit = 2,  // بستانکار
    Both = 3     // هر دو
}

/// <summary>نوع حساب (fin.Accounts.AccountType).</summary>
public enum AccountType : byte
{
    BalanceSheet = 1,   // ترازنامه‌ای
    ProfitAndLoss = 2,  // سود و زیانی
    Memorandum = 3      // انتظامی
}
