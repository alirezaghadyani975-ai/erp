using Erp.SharedKernel.Security;

namespace Erp.Security.Application;

/// <summary>مجوزهای مدیریت سیستم.</summary>
public static class SecurityPermissions
{
    public const string Users = "sec.user.manage";
    public const string Roles = "sec.role.manage";
    public const string Companies = "sec.company.manage";
    public const string Log = "sec.log.view";
    public const string Settings = "sec.settings.edit";
    public const string AuditHistory = "sys.audit.history";
    public const string AuditReport = "sys.audit.report";
    public const string Excel = "sys.export.excel";

    public static IPermissionSource List { get; } = new PermissionList("مدیریت سیستم")
        .Add("sec.user", "کاربران", ("manage", "مدیریت"))
        .Add("sec.role", "نقش‌ها و مجوزها", ("manage", "مدیریت"))
        .Add("sec.company", "شرکت‌ها", ("manage", "مدیریت"))
        .Add("sec.log", "لاگ امنیتی", ("view", "مشاهده"))
        .Add("sec.settings", "تنظیمات امنیتی", ("edit", "اصلاح"))
        .Add("sys.audit", "لاگ تغییرات", ("history", "تاریخچه فرم‌ها"), ("report", "گزارش کامل"))
        .Add("sys.export", "خروجی گرفتن", ("excel", "خروجی اکسل گزارش‌ها و فهرست‌ها"));
}

/// <summary>همه مجوزهای تعریف‌شده در ماژول‌ها (برای درخت مجوز نقش و پیام «مجوز ... را ندارید»).</summary>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDef> _byKey;

    public PermissionCatalog(IEnumerable<IPermissionSource> sources)
    {
        All = sources.SelectMany(s => s.Permissions).ToList();
        _byKey = new Dictionary<string, PermissionDef>(StringComparer.Ordinal);
        foreach (var p in All)
            if (!_byKey.TryAdd(p.Key, p))
                throw new InvalidOperationException($"کلید مجوز «{p.Key}» دو بار تعریف شده است.");
    }

    public IReadOnlyList<PermissionDef> All { get; }

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public string Title(string key) => _byKey.TryGetValue(key, out var p) ? p.Title : key;

    /// <summary>
    /// مجوز «مشاهده» لازمه هر کار دیگر روی همان فرم است (بدون آن فهرست و خود فرم باز نمی‌شود)؛
    /// برای هر کلید a.b.x اگر a.b.view تعریف شده باشد، آن هم اضافه می‌شود.
    /// </summary>
    public HashSet<string> WithImplied(IEnumerable<string> keys)
    {
        var result = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in result.ToList())
        {
            var view = ViewOf(key);
            if (view is not null && _byKey.ContainsKey(view)) result.Add(view);
        }
        return result;
    }

    /// <summary>کلید «مشاهده» همان فرم (برای خود کلید مشاهده: null).</summary>
    public static string? ViewOf(string key)
    {
        var dot = key.LastIndexOf('.');
        return dot < 0 || key[(dot + 1)..] == "view" ? null : key[..dot] + ".view";
    }
}
