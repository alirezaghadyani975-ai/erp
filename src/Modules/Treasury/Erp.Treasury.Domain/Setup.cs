using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Treasury.Domain;

internal static class Texts
{
    public static string Required(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");
        return v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }

    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }

    public static string? Digits(string? value, string label, int? exactLength = null, int max = 30)
    {
        var v = PersianText.NormalizeOrNull(value)?.Replace(" ", "").Replace("-", "");
        if (v is null) return null;
        if (!v.All(char.IsAsciiDigit)) throw new DomainException($"{label} فقط رقم است.");
        if (exactLength is int n && v.Length != n) throw new DomainException($"{label} باید {n} رقم باشد.");
        if (v.Length > max) throw new DomainException($"{label} حداکثر {max} رقم است.");
        return v;
    }
}

/// <summary>صندوق (trs.CashBoxes)؛ وجه نقد و چک‌های دریافتی در صندوق نگهداری می‌شوند.</summary>
public class CashBox : Entity<int>
{
    private CashBox() { } // EF

    public CashBox(string title, string? notes) => Update(title, notes);

    public string Title { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public void Update(string title, string? notes)
    {
        Title = Texts.Required(title, "عنوان صندوق", 100);
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void SetActive(bool active) => IsActive = active;
}

public enum BankAccountKind : byte
{
    Current = 1,      // جاری
    ShortTerm = 2,    // سپرده کوتاه‌مدت
    Gharzolhasaneh = 3 // قرض‌الحسنه
}

/// <summary>حساب بانکی شرکت (trs.BankAccounts).</summary>
public class BankAccount : Entity<int>
{
    private BankAccount() { } // EF

    public BankAccount(string title) => Title = Texts.Required(title, "عنوان حساب", 150);

    public string Title { get; private set; } = null!;
    public short? BankId { get; private set; }
    public string? Branch { get; private set; }
    public string? AccountNo { get; private set; }
    public string? Iban { get; private set; }
    public BankAccountKind AccountKind { get; private set; } = BankAccountKind.Current;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public void Update(string title, short? bankId, string? branch, string? accountNo, string? iban, BankAccountKind kind, string? notes)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع حساب بانکی معتبر نیست.");
        Title = Texts.Required(title, "عنوان حساب", 150);
        BankId = bankId;
        Branch = Texts.Optional(branch, "شعبه", 100);
        AccountNo = Texts.Digits(accountNo, "شماره حساب");
        var ibanText = PersianText.NormalizeOrNull(iban)?.Replace(" ", "").ToUpperInvariant();
        if (ibanText is not null)
        {
            if (!ibanText.StartsWith("IR")) ibanText = "IR" + ibanText;
            if (ibanText.Length != 26 || !ibanText[2..].All(char.IsAsciiDigit))
                throw new DomainException("شماره شبا باید IR و ۲۴ رقم باشد.");
        }
        Iban = ibanText;
        AccountKind = kind;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>تنخواه‌گردان (trs.ImprestFunds) با شخص دارنده.</summary>
public class ImprestFund : Entity<int>
{
    private ImprestFund() { } // EF

    public ImprestFund(string title, int? holderPersonId, string? notes) => Update(title, holderPersonId, notes);

    public string Title { get; private set; } = null!;
    public int? HolderPersonId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public void Update(string title, int? holderPersonId, string? notes)
    {
        Title = Texts.Required(title, "عنوان تنخواه‌گردان", 100);
        HolderPersonId = holderPersonId;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void SetActive(bool active) => IsActive = active;
}

public enum TransactionKind : byte
{
    Receipt = 1,
    Payment = 2
}

/// <summary>نوع دریافت یا پرداخت (trs.TransactionTypes): معین طرف مقابل در سند.</summary>
public class TransactionType : Entity<short>
{
    private TransactionType() { } // EF

    public TransactionType(TransactionKind kind, string title, int accountId, int sortOrder)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع معتبر نیست.");
        Kind = kind;
        Update(title, accountId, sortOrder);
    }

    public TransactionKind Kind { get; private set; }
    public string Title { get; private set; } = null!;
    public int AccountId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string title, int accountId, int sortOrder)
    {
        if (accountId <= 0) throw new DomainException("معین طرف حساب الزامی است.");
        Title = Texts.Required(title, "عنوان", 100);
        AccountId = accountId;
        SortOrder = sortOrder;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>پایه فرم‌های خزانه‌داری: شماره سیستمی (۶ رقمی در نمایش)، تاریخ، شرح.</summary>
public abstract class TreasuryDocument : AggregateRoot<long>
{
    public int DocNo { get; protected set; }
    public DateOnly DocDate { get; protected set; }
    public string? Description { get; protected set; }
    public int CreatedBy { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public byte[] RowVer { get; private set; } = [];

    protected void Init(int docNo, int createdBy, DateTime nowUtc)
    {
        if (docNo <= 0) throw new DomainException("شماره فرم نامعتبر است.");
        DocNo = docNo;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
    }

    protected void SetBase(DateOnly date, string? description)
    {
        DocDate = date;
        Description = Texts.Optional(description, "شرح", 500);
    }
}
