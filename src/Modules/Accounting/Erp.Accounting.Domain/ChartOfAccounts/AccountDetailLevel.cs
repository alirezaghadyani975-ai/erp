namespace Erp.Accounting.Domain.ChartOfAccounts;

/// <summary>یک سطح تفصیلی فعال برای یک معین (fin.AccountDetailLevels).</summary>
public class AccountDetailLevel
{
    private readonly List<AccountDetailLevelType> _allowedTypes = [];

    private AccountDetailLevel() { } // EF

    internal AccountDetailLevel(int accountId, byte levelNo, bool isRequired, IEnumerable<short> detailTypeIds)
    {
        AccountId = accountId;
        LevelNo = levelNo;
        IsRequired = isRequired;
        foreach (var id in detailTypeIds.Distinct())
            _allowedTypes.Add(new AccountDetailLevelType(levelNo, id));
    }

    public int AccountId { get; private set; }
    public byte LevelNo { get; private set; }
    public bool IsRequired { get; private set; }

    public IReadOnlyCollection<AccountDetailLevelType> AllowedTypes => _allowedTypes.AsReadOnly();

    internal void Update(bool isRequired, IReadOnlyCollection<short> detailTypeIds)
    {
        IsRequired = isRequired;
        _allowedTypes.RemoveAll(t => !detailTypeIds.Contains(t.DetailTypeId));
        foreach (var id in detailTypeIds.Distinct().Where(id => _allowedTypes.All(t => t.DetailTypeId != id)))
            _allowedTypes.Add(new AccountDetailLevelType(LevelNo, id));
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
