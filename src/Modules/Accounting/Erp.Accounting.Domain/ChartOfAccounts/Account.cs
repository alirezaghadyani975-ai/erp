using Erp.SharedKernel.Text;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.ChartOfAccounts;

/// <summary>
/// حساب در درخت کدینگ (fin.Accounts): گروه ← کل ← معین.
/// حساب‌های استاندارد از قالب کدینگ می‌آیند و قفل‌اند: کدشان عوض نمی‌شود و حذف نمی‌شوند،
/// فقط عنوانشان قابل تغییر و قابل غیرفعال‌شدن است. شرکت می‌تواند حساب اختصاصی اضافه کند.
/// فقط معین در سند استفاده می‌شود؛ تفصیلی‌های مجاز هر معین در <see cref="DetailLevels"/> است.
/// </summary>
public class Account : AggregateRoot<int>
{
    public const int MaxDetailLevels = 6;

    private readonly List<AccountDetailLevel> _detailLevels = [];

    private Account() { } // EF

    public int? ParentId { get; private set; }
    public AccountLevel Level { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public AccountNature? Nature { get; private set; }
    public AccountType? Type { get; private set; }
    public bool IsStandard { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<AccountDetailLevel> DetailLevels => _detailLevels.AsReadOnly();

    /// <summary>حساب اختصاصی شرکت (کل یا معین) زیر یک حساب موجود.</summary>
    public static Account CreateCustom(Account parent, string code, string title,
        AccountNature? nature = null, AccountType? type = null)
    {
        if (parent.Level == AccountLevel.Subsidiary)
            throw new DomainException("زیر حساب معین نمی‌توان حساب تعریف کرد؛ برای سطوح پایین‌تر از تفصیلی استفاده کنید.");
        if (!parent.IsActive)
            throw new DomainException($"حساب «{parent.Title}» غیرفعال است.");

        var level = parent.Level + 1;
        code = NormalizeCode(code);
        if (!code.StartsWith(parent.Code, StringComparison.Ordinal) || code.Length <= parent.Code.Length)
            throw new DomainException($"کد حساب اختصاصی باید با کد والد ({parent.Code}) شروع شود.");

        if (level == AccountLevel.Subsidiary && (nature is null || type is null))
            throw new DomainException("برای حساب معین، ماهیت و نوع حساب الزامی است.");

        return new Account
        {
            ParentId = parent.Id,
            Level = level,
            Code = code,
            Title = NormalizeTitle(title),
            Nature = nature,
            Type = type,
            IsStandard = false
        };
    }

    public void Rename(string title) => Title = NormalizeTitle(title);

    public void ChangeNature(AccountNature nature)
    {
        EnsureSubsidiary();
        Nature = nature;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    // ---------- قواعد تفصیلی ----------

    /// <summary>
    /// فعال‌کردن یا تغییر یک سطح تفصیلی برای این معین و تعیین انواع تفصیلی مجاز در آن.
    /// <paramref name="levelsInUse"/> سطح‌هایی است که روی این معین گردش دارند (از دیتابیس).
    /// </summary>
    public void SetDetailLevel(int levelNo, bool isRequired, IReadOnlyCollection<short> detailTypeIds,
        IReadOnlyDictionary<int, IReadOnlySet<short>>? typesInUse = null, IReadOnlyCollection<short>? roleIds = null)
    {
        EnsureSubsidiary();
        if (levelNo is < 1 or > MaxDetailLevels)
            throw new DomainException($"سطح تفصیلی باید بین ۱ تا {MaxDetailLevels} باشد.");
        if (detailTypeIds.Count == 0)
            throw new DomainException("حداقل یک نوع تفصیلی برای سطح لازم است.");
        if (levelNo > 1 && _detailLevels.All(l => l.LevelNo != levelNo - 1))
            throw new DomainException($"سطح {levelNo} را فقط بعد از تعریف سطح {levelNo - 1} می‌توان اضافه کرد.");

        var existing = _detailLevels.FirstOrDefault(l => l.LevelNo == levelNo);
        if (existing is null)
        {
            _detailLevels.Add(new AccountDetailLevel(Id, (byte)levelNo, isRequired, detailTypeIds, roleIds ?? []));
            return;
        }

        if (typesInUse?.TryGetValue(levelNo, out var used) == true)
        {
            var removed = used.Where(t => !detailTypeIds.Contains(t)).ToList();
            if (removed.Count > 0)
                throw new DomainException($"نوع تفصیلی‌ای که در سطح {levelNo} این معین گردش دارد از قاعده حذف نمی‌شود.");
        }

        existing.Update(isRequired, detailTypeIds, roleIds ?? []);
    }

    public void RemoveDetailLevel(int levelNo, bool hasTurnover)
    {
        EnsureSubsidiary();
        if (hasTurnover)
            throw new DomainException($"سطح {levelNo} روی این معین گردش دارد و حذف نمی‌شود.");
        if (_detailLevels.Any(l => l.LevelNo > levelNo))
            throw new DomainException("ابتدا سطح‌های بعدی را حذف کنید.");
        _detailLevels.RemoveAll(l => l.LevelNo == levelNo);
    }

    public bool AcceptsDetail(int levelNo, short detailTypeId) =>
        _detailLevels.Any(l => l.LevelNo == levelNo && l.AllowedTypes.Any(t => t.DetailTypeId == detailTypeId));

    public bool IsDetailLevelRequired(int levelNo) =>
        _detailLevels.Any(l => l.LevelNo == levelNo && l.IsRequired);

    private void EnsureSubsidiary()
    {
        if (Level != AccountLevel.Subsidiary)
            throw new DomainException("این تنظیم فقط برای حساب معین قابل انجام است.");
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("کد حساب الزامی است.");
        code = code.Trim();
        if (!code.All(char.IsAsciiDigit))
            throw new DomainException("کد حساب فقط می‌تواند شامل رقم باشد.");
        return code;
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان حساب الزامی است.");
        return PersianText.Normalize(title);
    }
}
