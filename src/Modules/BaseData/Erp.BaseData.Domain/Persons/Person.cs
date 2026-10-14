using Erp.BaseData.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.SharedKernel.Validation;

namespace Erp.BaseData.Domain.Persons;

/// <summary>
/// اطلاعات قابل ویرایش شخص. برای حقیقی نام و نام خانوادگی، برای حقوقی نام شرکت لازم است؛
/// بقیه اختیاری‌اند ولی اگر وارد شوند قالب و رقم کنترلشان بررسی می‌شود.
/// فیلدهای *Id به اقلام جدول پایه اشاره می‌کنند (درستی فهرستشان در سرویس کنترل می‌شود).
/// </summary>
public sealed class PersonProfile
{
    // حقیقی
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? FatherName { get; init; }
    public Gender? Gender { get; init; }
    public DateOnly? BirthDate { get; init; }
    public int? BirthCityId { get; init; }
    public string? IdNumber { get; init; }
    public int? IdIssueCityId { get; init; }
    public MaritalStatus? MaritalStatus { get; init; }
    public bool IsForeign { get; init; }
    public int? NationalityId { get; init; }
    public string? ForeignNationalCode { get; init; }
    public string? PassportNo { get; init; }

    // حقوقی
    public string? LegalName { get; init; }
    public LegalType? LegalType { get; init; }
    public string? RegistrationNo { get; init; }
    public DateOnly? RegistrationDate { get; init; }
    public int? RegistrationCityId { get; init; }

    // مشترک
    /// <summary>کد ملی (حقیقی) یا شناسه ملی (حقوقی).</summary>
    public string? NationalId { get; init; }
    public string? EconomicCode { get; init; }
    public TaxPayerType? TaxPayerType { get; init; }
    public string? BranchCode { get; init; }
    public int? PersonGroupId { get; init; }
    public string? Mobile { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? Notes { get; init; }
}

public sealed record AddressData(int? AddressTypeId, int? ProvinceId, int? CityId, string Address, string? PostalCode,
    string? Phone, bool IsDefault);

public sealed record BankAccountData(short? BankId, string? Iban, string? AccountNo, string? CardNo, bool IsDefault);

public sealed record ContactData(string FullName, int? ContactRoleId, string? Mobile, string? Phone, string? Email, string? Notes);

/// <summary>
/// شخص حقیقی یا حقوقی (base.Persons) با نقش‌ها، نشانی‌ها، حساب‌های بانکی و رابطین.
/// پیوست‌ها جدا (PersonAttachment) نگه داشته می‌شوند تا محتوای فایل با شخص بارگذاری نشود.
/// </summary>
public class Person : AggregateRoot<int>
{
    private readonly List<PersonRole> _roles = [];
    private readonly List<PersonAddress> _addresses = [];
    private readonly List<PersonBankAccount> _bankAccounts = [];
    private readonly List<PersonContact> _contacts = [];

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
    public int? BirthCityId { get; private set; }
    public string? IdNumber { get; private set; }
    public int? IdIssueCityId { get; private set; }
    public MaritalStatus? MaritalStatus { get; private set; }
    public bool IsForeign { get; private set; }
    public int? NationalityId { get; private set; }
    public string? ForeignNationalCode { get; private set; }
    public string? PassportNo { get; private set; }

    public LegalType? LegalType { get; private set; }
    public string? RegistrationNo { get; private set; }
    public DateOnly? RegistrationDate { get; private set; }
    public int? RegistrationCityId { get; private set; }

    public TaxPayerType? TaxPayerType { get; private set; }
    public string? BranchCode { get; private set; }
    public int? PersonGroupId { get; private set; }
    public string? Mobile { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<PersonRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<PersonAddress> Addresses => _addresses.AsReadOnly();
    public IReadOnlyCollection<PersonBankAccount> BankAccounts => _bankAccounts.AsReadOnly();
    public IReadOnlyCollection<PersonContact> Contacts => _contacts.AsReadOnly();

    public void Update(PersonProfile profile, DateOnly today) => Apply(profile, today);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool HasRole(short roleId) => _roles.Any(r => r.RoleId == roleId);

    /// <summary>
    /// نقش‌ها را جایگزین می‌کند. کارمند فقط شخص حقیقی است و کد ملی لازم دارد
    /// (اطلاعات کارگزینی بعداً در ماژول خودش ثبت می‌شود).
    /// </summary>
    public void SetRoles(IReadOnlyCollection<short> roleIds, short employeeRoleId)
    {
        if (roleIds.Count == 0)
            throw new DomainException("حداقل یک نقش (مشتری، تأمین‌کننده، کارمند، ...) برای شخص لازم است.");
        if (roleIds.Contains(employeeRoleId))
        {
            if (Kind != PersonKind.Natural)
                throw new DomainException("نقش کارمند فقط برای شخص حقیقی است.");
            if (!IsForeign && NationalId is null)
                throw new DomainException("برای کارمند کد ملی لازم است (بیمه و مالیات حقوق).");
        }

        _roles.RemoveAll(r => !roleIds.Contains(r.RoleId));
        foreach (var id in roleIds.Distinct().Where(id => !HasRole(id)))
            _roles.Add(new PersonRole(id));
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

    public void SetContacts(IEnumerable<ContactData> contacts)
    {
        var list = contacts.Select((c, i) => PersonContact.Create(c, i + 1)).ToList();
        _contacts.Clear();
        _contacts.AddRange(list);
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
            BirthCityId = p.BirthCityId;
            IdNumber = Digits(p.IdNumber, "شماره شناسنامه", 10);
            IdIssueCityId = p.IdIssueCityId;
            MaritalStatus = p.MaritalStatus;
            IsForeign = p.IsForeign;
            NationalityId = IsForeign ? p.NationalityId : null;
            ForeignNationalCode = IsForeign ? Checked(IdKind.ForeignNationalCode, p.ForeignNationalCode) : null;
            PassportNo = IsForeign ? Text(p.PassportNo)?.ToUpperInvariant() : null;
            NationalId = IsForeign ? null : Checked(IdKind.NationalCode, p.NationalId);
            if (IsForeign && ForeignNationalCode is null && PassportNo is null)
                throw new DomainException("برای تبعه خارجی کد فراگیر یا شماره گذرنامه لازم است.");

            LegalType = null;
            RegistrationNo = null;
            RegistrationDate = null;
            RegistrationCityId = null;
        }
        else
        {
            DisplayName = Required(p.LegalName, "نام شخص حقوقی");
            NationalId = Checked(IdKind.LegalNationalId, p.NationalId);
            LegalType = p.LegalType;
            RegistrationNo = Text(p.RegistrationNo);
            RegistrationDate = PastDate(p.RegistrationDate, today, "تاریخ ثبت");
            RegistrationCityId = p.RegistrationCityId;

            FirstName = LastName = FatherName = IdNumber = ForeignNationalCode = PassportNo = null;
            BirthCityId = IdIssueCityId = NationalityId = null;
            Gender = null;
            BirthDate = null;
            MaritalStatus = null;
            IsForeign = false;
        }

        EconomicCode = Checked(IdKind.EconomicCode, p.EconomicCode);
        TaxPayerType = p.TaxPayerType ?? (Kind == PersonKind.Legal ? Persons.TaxPayerType.Legal
            : IsForeign ? Persons.TaxPayerType.Foreign : Persons.TaxPayerType.Natural);
        BranchCode = Checked(IdKind.BranchCode, p.BranchCode);
        PersonGroupId = p.PersonGroupId;
        Mobile = Checked(IdKind.Mobile, p.Mobile);
        Phone = Checked(IdKind.Phone, p.Phone);
        Email = Checked(IdKind.Email, p.Email);
        Website = Text(p.Website);
        Notes = Text(p.Notes);
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
                AddressTypeId = a.AddressTypeId,
                ProvinceId = a.ProvinceId,
                CityId = a.CityId,
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
    public int? AddressTypeId { get; private set; }
    public int? ProvinceId { get; private set; }
    public int? CityId { get; private set; }
    public string Address { get; private set; } = null!;
    public string? PostalCode { get; private set; }
    public string? Phone { get; private set; }
    public bool IsDefault { get; internal set; }
}

/// <summary>حساب بانکی شخص (base.PersonBankAccounts). بانک اگر انتخاب نشود از شبا یا کارت تشخیص داده می‌شود (در سرویس).</summary>
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
            return new PersonBankAccount { Iban = iban, CardNo = card, AccountNo = accountNo, BankId = a.BankId, IsDefault = a.IsDefault };
        }
        catch (DomainException ex)
        {
            throw new DomainException($"حساب بانکی {row}: {ex.Message}");
        }
    }

    public int PersonId { get; private set; }
    public short? BankId { get; private set; }
    public string? Iban { get; private set; }
    public string? AccountNo { get; private set; }
    public string? CardNo { get; private set; }
    public bool IsDefault { get; internal set; }
}

/// <summary>رابط شخص (base.PersonContacts)، معمولاً برای اشخاص حقوقی.</summary>
public class PersonContact : Entity<int>
{
    private PersonContact() { } // EF

    internal static PersonContact Create(ContactData c, int row)
    {
        try
        {
            return new PersonContact
            {
                FullName = Person.Required(c.FullName, "نام رابط"),
                ContactRoleId = c.ContactRoleId,
                Mobile = Person.Checked(IdKind.Mobile, c.Mobile),
                Phone = Person.Checked(IdKind.Phone, c.Phone),
                Email = Person.Checked(IdKind.Email, c.Email),
                Notes = Person.Text(c.Notes)
            };
        }
        catch (DomainException ex)
        {
            throw new DomainException($"رابط {row}: {ex.Message}");
        }
    }

    public int PersonId { get; private set; }
    public string FullName { get; private set; } = null!;
    public int? ContactRoleId { get; private set; }
    public string? Mobile { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Notes { get; private set; }
}

/// <summary>پیوست مدرک شخص (base.PersonAttachments)؛ حداکثر ۱۰ مگابایت.</summary>
public class PersonAttachment : Entity<int>
{
    public const int MaxBytes = 10 * 1024 * 1024;

    private PersonAttachment() { } // EF

    public PersonAttachment(int personId, string title, string fileName, string contentType, byte[] content, int uploadedBy)
    {
        if (content.Length == 0) throw new DomainException("فایل خالی است.");
        if (content.Length > MaxBytes) throw new DomainException("حجم فایل حداکثر ۱۰ مگابایت است.");
        PersonId = personId;
        FileName = Path.GetFileName(fileName.Trim());
        Title = Person.Text(title) ?? Path.GetFileNameWithoutExtension(FileName);
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        Content = content;
        SizeBytes = content.Length;
        UploadedBy = uploadedBy;
    }

    public int PersonId { get; private set; }
    public string Title { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public int SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];
    public DateTime UploadedAt { get; private set; }
    public int UploadedBy { get; private set; }
}
