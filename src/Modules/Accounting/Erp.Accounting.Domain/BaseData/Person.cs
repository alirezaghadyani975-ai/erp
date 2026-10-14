using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.SharedKernel.Validation;

namespace Erp.Accounting.Domain.BaseData;

/// <summary>
/// اطلاعات قابل ویرایش شخص. برای حقیقی نام و نام خانوادگی، برای حقوقی نام شرکت لازم است؛
/// بقیه اختیاری‌اند ولی اگر وارد شوند قالب و رقم کنترلشان بررسی می‌شود.
/// </summary>
public sealed class PersonProfile
{
    // حقیقی
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? FatherName { get; init; }
    public Gender? Gender { get; init; }
    public DateOnly? BirthDate { get; init; }
    public string? BirthPlace { get; init; }
    public string? IdNumber { get; init; }
    public string? IdIssuePlace { get; init; }
    public MaritalStatus? MaritalStatus { get; init; }
    public bool IsForeign { get; init; }
    public string? Nationality { get; init; }
    public string? ForeignNationalCode { get; init; }
    public string? PassportNo { get; init; }

    // حقوقی
    public string? LegalName { get; init; }
    public LegalType? LegalType { get; init; }
    public string? RegistrationNo { get; init; }
    public DateOnly? RegistrationDate { get; init; }
    public string? RegistrationPlace { get; init; }

    // مشترک
    /// <summary>کد ملی (حقیقی) یا شناسه ملی (حقوقی).</summary>
    public string? NationalId { get; init; }
    public string? EconomicCode { get; init; }
    public TaxPayerType? TaxPayerType { get; init; }
    public string? BranchCode { get; init; }
    public string? Mobile { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? Notes { get; init; }
}

public sealed record AddressData(string? Title, string? Province, string? City, string Address, string? PostalCode,
    string? Phone, bool IsDefault);

public sealed record BankAccountData(string? BankName, string? Iban, string? AccountNo, string? CardNo, bool IsDefault);

/// <summary>
/// شخص حقیقی یا حقوقی (base.Persons) با نقش‌ها، نشانی‌ها، حساب‌های بانکی و اطلاعات پرسنلی.
/// هر شخص یک تفصیلی مشترک برای همه نقش‌های طرف‌حسابی دارد؛ نقش کارمند تفصیلی جدا دارد.
/// </summary>
public class Person : AggregateRoot<int>
{
    private readonly List<PersonRole> _roles = [];
    private readonly List<PersonAddress> _addresses = [];
    private readonly List<PersonBankAccount> _bankAccounts = [];

    private Person() { } // EF

    public static Person Create(PersonKind kind, PersonProfile profile, DateOnly today)
    {
        var person = new Person { Kind = kind };
        person.Apply(profile, today);
        return person;
    }

    public PersonKind Kind { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string? NationalId { get; private set; }
    public string? EconomicCode { get; private set; }
    public bool IsActive { get; private set; } = true;

    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? FatherName { get; private set; }
    public Gender? Gender { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? BirthPlace { get; private set; }
    public string? IdNumber { get; private set; }
    public string? IdIssuePlace { get; private set; }
    public MaritalStatus? MaritalStatus { get; private set; }
    public bool IsForeign { get; private set; }
    public string? Nationality { get; private set; }
    public string? ForeignNationalCode { get; private set; }
    public string? PassportNo { get; private set; }

    public LegalType? LegalType { get; private set; }
    public string? RegistrationNo { get; private set; }
    public DateOnly? RegistrationDate { get; private set; }
    public string? RegistrationPlace { get; private set; }

    public TaxPayerType? TaxPayerType { get; private set; }
    public string? BranchCode { get; private set; }
    public string? Mobile { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<PersonRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<PersonAddress> Addresses => _addresses.AsReadOnly();
    public IReadOnlyCollection<PersonBankAccount> BankAccounts => _bankAccounts.AsReadOnly();
    public Employee? Employee { get; private set; }

    public void Update(PersonProfile profile, DateOnly today) => Apply(profile, today);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool HasRole(short roleId) => _roles.Any(r => r.RoleId == roleId);

    /// <summary>نقش‌ها را جایگزین می‌کند. کارمند فقط شخص حقیقی است و اطلاعات پرسنلی‌اش ساخته/حذف می‌شود.</summary>
    public void SetRoles(IReadOnlyCollection<short> roleIds, short employeeRoleId)
    {
        if (roleIds.Count == 0)
            throw new DomainException("حداقل یک نقش (مشتری، تأمین‌کننده، کارمند، ...) برای شخص لازم است.");
        if (roleIds.Contains(employeeRoleId) && Kind != PersonKind.Natural)
            throw new DomainException("نقش کارمند فقط برای شخص حقیقی است.");

        _roles.RemoveAll(r => !roleIds.Contains(r.RoleId));
        foreach (var id in roleIds.Distinct().Where(id => !HasRole(id)))
            _roles.Add(new PersonRole(id));

        if (roleIds.Contains(employeeRoleId))
        {
            if (!IsForeign && NationalId is null)
                throw new DomainException("برای کارمند کد ملی لازم است (بیمه و مالیات حقوق).");
            Employee ??= new Employee();
        }
        else Employee = null;
    }

    public void SetEmployee(EmployeeData data, DateOnly today)
    {
        if (Employee is null)
            throw new DomainException("اطلاعات پرسنلی فقط برای شخص با نقش کارمند ثبت می‌شود.");
        Employee.Apply(data, today);
    }

    public void SetAddresses(IEnumerable<AddressData> addresses)
    {
        var list = addresses.Select((a, i) => PersonAddress.Create(a, i + 1)).ToList();
        EnsureSingleDefault(list, a => a.IsDefault, (a, v) => a.IsDefault = v);
        _addresses.Clear();
        _addresses.AddRange(list);
    }

    public void SetBankAccounts(IEnumerable<BankAccountData> accounts)
    {
        var list = accounts.Select((a, i) => PersonBankAccount.Create(a, i + 1)).ToList();
        if (list.Where(a => a.Iban is not null).GroupBy(a => a.Iban).Any(g => g.Count() > 1))
            throw new DomainException("یک شبا دو بار وارد شده است.");
        EnsureSingleDefault(list, a => a.IsDefault, (a, v) => a.IsDefault = v);
        _bankAccounts.Clear();
        _bankAccounts.AddRange(list);
    }

    private static void EnsureSingleDefault<T>(List<T> items, Func<T, bool> get, Action<T, bool> set)
    {
        if (items.Count == 0) return;
        var defaults = items.Where(get).ToList();
        if (defaults.Count > 1)
            throw new DomainException("فقط یک مورد می‌تواند پیش‌فرض باشد.");
        if (defaults.Count == 0) set(items[0], true);
    }

    private void Apply(PersonProfile p, DateOnly today)
    {
        if (Kind == PersonKind.Natural)
        {
            FirstName = Required(p.FirstName, "نام");
            LastName = Required(p.LastName, "نام خانوادگی");
            DisplayName = $"{FirstName} {LastName}";
            FatherName = Text(p.FatherName);
            Gender = p.Gender;
            BirthDate = PastDate(p.BirthDate, today, "تاریخ تولد");
            if (BirthDate is { } b && b < today.AddYears(-120))
                throw new DomainException("تاریخ تولد معتبر نیست.");
            BirthPlace = Text(p.BirthPlace);
            IdNumber = Digits(p.IdNumber, "شماره شناسنامه", 10);
            IdIssuePlace = Text(p.IdIssuePlace);
            MaritalStatus = p.MaritalStatus;
            IsForeign = p.IsForeign;
            Nationality = IsForeign ? Text(p.Nationality) : null;
            ForeignNationalCode = IsForeign ? Checked(IdKind.ForeignNationalCode, p.ForeignNationalCode) : null;
            PassportNo = IsForeign ? Text(p.PassportNo)?.ToUpperInvariant() : null;
            NationalId = IsForeign ? null : Checked(IdKind.NationalCode, p.NationalId);
            if (IsForeign && ForeignNationalCode is null && PassportNo is null)
                throw new DomainException("برای تبعه خارجی کد فراگیر یا شماره گذرنامه لازم است.");

            LegalType = null;
            RegistrationNo = null;
            RegistrationDate = null;
            RegistrationPlace = null;
        }
        else
        {
            DisplayName = Required(p.LegalName, "نام شخص حقوقی");
            NationalId = Checked(IdKind.LegalNationalId, p.NationalId);
            LegalType = p.LegalType;
            RegistrationNo = Text(p.RegistrationNo);
            RegistrationDate = PastDate(p.RegistrationDate, today, "تاریخ ثبت");
            RegistrationPlace = Text(p.RegistrationPlace);

            FirstName = LastName = FatherName = BirthPlace = IdNumber = IdIssuePlace = null;
            Nationality = ForeignNationalCode = PassportNo = null;
            Gender = null;
            BirthDate = null;
            MaritalStatus = null;
            IsForeign = false;
        }

        EconomicCode = Checked(IdKind.EconomicCode, p.EconomicCode);
        TaxPayerType = p.TaxPayerType ?? (Kind == PersonKind.Legal ? BaseData.TaxPayerType.Legal
            : IsForeign ? BaseData.TaxPayerType.Foreign : BaseData.TaxPayerType.Natural);
        BranchCode = Checked(IdKind.BranchCode, p.BranchCode);
        Mobile = Checked(IdKind.Mobile, p.Mobile);
        Phone = Checked(IdKind.Phone, p.Phone);
        Email = Checked(IdKind.Email, p.Email);
        Website = Text(p.Website);
        Notes = Text(p.Notes);

        if (Employee is not null && !IsForeign && NationalId is null)
            throw new DomainException("برای کارمند کد ملی لازم است (بیمه و مالیات حقوق).");
    }

    // ---------- کمکی‌های اعتبارسنجی (مشترک با بخش‌های دیگر شخص) ----------

    internal static string Required(string? value, string label) =>
        PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");

    internal static string? Text(string? value) => PersianText.NormalizeOrNull(value);

    internal static string? Checked(IdKind kind, string? value)
    {
        var error = IranianIds.Error(kind, value);
        if (error is not null) throw new DomainException(error);
        return IranianIds.Normalize(kind, value);
    }

    internal static string? Digits(string? value, string label, int maxLength)
    {
        var v = IranianIds.Digits(value);
        if (v.Length == 0) return null;
        if (v.Length > maxLength || !v.All(char.IsAsciiDigit))
            throw new DomainException($"{label} باید عددی و حداکثر {maxLength} رقم باشد.");
        return v;
    }

    internal static DateOnly? PastDate(DateOnly? date, DateOnly today, string label) =>
        date > today ? throw new DomainException($"{label} نمی‌تواند در آینده باشد.") : date;
}

/// <summary>نقش شخص (base.PersonRoles).</summary>
public class PersonRole
{
    private PersonRole() { } // EF

    internal PersonRole(short roleId) => RoleId = roleId;

    public int PersonId { get; private set; }
    public short RoleId { get; private set; }
}

/// <summary>نوع نقش (base.PersonRoleTypes): نقش‌های سیستمی و نقش‌هایی که کاربر اضافه می‌کند.</summary>
public class PersonRoleType : Entity<short>
{
    private PersonRoleType() { } // EF

    public PersonRoleType(string title, short sortOrder = 100)
    {
        Title = Person.Required(title, "عنوان نقش");
        SortOrder = sortOrder;
    }

    public string? Code { get; private set; }
    public string Title { get; private set; } = null!;
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;
    public short SortOrder { get; private set; }

    public void Rename(string title) => Title = Person.Required(title, "عنوان نقش");

    public void Deactivate()
    {
        if (IsSystem) throw new DomainException("نقش‌های سیستمی غیرفعال نمی‌شوند.");
        IsActive = false;
    }

    public void Activate() => IsActive = true;
}

/// <summary>نشانی شخص (base.PersonAddresses).</summary>
public class PersonAddress : Entity<int>
{
    private PersonAddress() { } // EF

    internal static PersonAddress Create(AddressData a, int row)
    {
        try
        {
            return new PersonAddress
            {
                Title = Person.Text(a.Title),
                Province = Person.Text(a.Province),
                City = Person.Text(a.City),
                Address = Person.Required(a.Address, "متن نشانی"),
                PostalCode = Person.Checked(IdKind.PostalCode, a.PostalCode),
                Phone = Person.Checked(IdKind.Phone, a.Phone),
                IsDefault = a.IsDefault
            };
        }
        catch (DomainException ex)
        {
            throw new DomainException($"نشانی {row}: {ex.Message}");
        }
    }

    public int PersonId { get; private set; }
    public string? Title { get; private set; }
    public string? Province { get; private set; }
    public string? City { get; private set; }
    public string Address { get; private set; } = null!;
    public string? PostalCode { get; private set; }
    public string? Phone { get; private set; }
    public bool IsDefault { get; internal set; }
}

/// <summary>حساب بانکی شخص (base.PersonBankAccounts). نام بانک اگر خالی باشد از شبا یا کارت تشخیص داده می‌شود.</summary>
public class PersonBankAccount : Entity<int>
{
    private PersonBankAccount() { } // EF

    internal static PersonBankAccount Create(BankAccountData a, int row)
    {
        try
        {
            var iban = Person.Checked(IdKind.Iban, a.Iban);
            var card = Person.Checked(IdKind.CardNumber, a.CardNo);
            var accountNo = Person.Text(a.AccountNo);
            if (iban is null && card is null && accountNo is null)
                throw new DomainException("شبا، شماره حساب یا شماره کارت لازم است.");
            return new PersonBankAccount
            {
                Iban = iban,
                CardNo = card,
                AccountNo = accountNo,
                BankName = Person.Text(a.BankName) ?? IranianIds.BankFromIban(iban) ?? IranianIds.BankFromCard(card),
                IsDefault = a.IsDefault
            };
        }
        catch (DomainException ex)
        {
            throw new DomainException($"حساب بانکی {row}: {ex.Message}");
        }
    }

    public int PersonId { get; private set; }
    public string? BankName { get; private set; }
    public string? Iban { get; private set; }
    public string? AccountNo { get; private set; }
    public string? CardNo { get; private set; }
    public bool IsDefault { get; internal set; }
}
