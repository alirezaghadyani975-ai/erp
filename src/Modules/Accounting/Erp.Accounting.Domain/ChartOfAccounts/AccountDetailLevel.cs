namespace Erp.Accounting.Domain.ChartOfAccounts;

/// <summary>یک سطح تفصیلی فعال برای یک معین (fin.AccountDetailLevels).</summary>
public class AccountDetailLevel
{
    private readonly List<AccountDetailLevelType> _allowedTypes = [];
    private readonly List<AccountDetailLevelRole> _allowedRoles = [];

    private AccountDetailLevel() { } // EF

    internal AccountDetailLevel(int accountId, byte levelNo, bool isRequired, IEnumerable<short> detailTypeIds,
        IEnumerable<short> roleIds)
    {
        AccountId = accountId;
        LevelNo = levelNo;
        IsRequired = isRequired;
        foreach (var id in detailTypeIds.Distinct())
            _allowedTypes.Add(new AccountDetailLevelType(levelNo, id));
        foreach (var id in roleIds.Distinct())
            _allowedRoles.Add(new AccountDetailLevelRole(levelNo, id));
    }

    public int AccountId { get; private set; }
    public byte LevelNo { get; private set; }
    public bool IsRequired { get; private set; }

    public IReadOnlyCollection<AccountDetailLevelType> AllowedTypes => _allowedTypes.AsReadOnly();

    /// <summary>
    /// نقش‌هایی که فهرست اشخاص این سطح را فیلتر می‌کنند (فیلتر نرم: فقط پیش‌فرض فهرست، نه منع ثبت).
    /// خالی یعنی همه اشخاص.
    /// </summary>
    public IReadOnlyCollection<AccountDetailLevelRole> AllowedRoles => _allowedRoles.AsReadOnly();

    internal void Update(bool isRequired, IReadOnlyCollection<short> detailTypeIds, IReadOnlyCollection<short> roleIds)
    {
        IsRequired = isRequired;
        _allowedTypes.RemoveAll(t => !detailTypeIds.Contains(t.DetailTypeId));
        foreach (var id in detailTypeIds.Distinct().Where(id => _allowedTypes.All(t => t.DetailTypeId != id)))
            _allowedTypes.Add(new AccountDetailLevelType(LevelNo, id));
        _allowedRoles.RemoveAll(r => !roleIds.Contains(r.RoleId));
        foreach (var id in roleIds.Distinct().Where(id => _allowedRoles.All(r => r.RoleId != id)))
            _allowedRoles.Add(new AccountDetailLevelRole(LevelNo, id));
    }
}

/// <summary>نوع تفصیلی مجاز در یک سطح (fin.AccountDetailLevelTypes).</summary>
public class AccountDetailLevelType
{
    private AccountDetailLevelType() { } // EF

    internal AccountDetailLevelType(byte levelNo, short detailTypeId)
    {
        LevelNo = levelNo;
        DetailTypeId = detailTypeId;
    }

    public int AccountId { get; private set; }
    public byte LevelNo { get; private set; }
    public short DetailTypeId { get; private set; }
}

/// <summary>نقش مجاز در یک سطح (fin.AccountDetailLevelRoles).</summary>
public class AccountDetailLevelRole
{
    private AccountDetailLevelRole() { } // EF

    internal AccountDetailLevelRole(byte levelNo, short roleId)
    {
        LevelNo = levelNo;
        RoleId = roleId;
    }

    public int AccountId { get; private set; }
    public byte LevelNo { get; private set; }
    public short RoleId { get; private set; }
}
