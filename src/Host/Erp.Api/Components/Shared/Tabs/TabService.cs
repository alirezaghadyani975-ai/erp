using Microsoft.AspNetCore.Components;

namespace Erp.Api.Components.Shared.Tabs;

/// <summary>یک فرم باز: صفحه و پارامترهای مسیرش. با جابه‌جایی بین تب‌ها صفحه زنده می‌ماند و وضعیتش از دست نمی‌رود.</summary>
public sealed class TabItem
{
    public required string Key { get; set; }          // مسیر نسبی با query، مثل persons/12
    public required Type PageType { get; set; }
    public required IDictionary<string, object?> Parameters { get; set; }
    public string Title { get; set; } = "…";
    /// <summary>با عوض شدن صفحه داخل همان تب، نمونه تازه ساخته شود.</summary>
    public int Version { get; set; }
}

/// <summary>
/// تب‌های باز کاربر (در طول اتصال). هر مسیر جدید یک تب باز می‌کند؛ مسیر تکراری همان تب را جلو می‌آورد.
/// صفحه‌ها با <see cref="Go"/> می‌توانند در همان تب به مسیر دیگری بروند (مثلاً بعد از ذخیره رکورد جدید).
/// </summary>
public sealed class TabService(NavigationManager nav)
{
    public const int MaxTabs = 15;

    private readonly List<TabItem> _tabs = [];
    private string? _replaceKey;

    public IReadOnlyList<TabItem> Tabs => _tabs;
    public string? ActiveKey { get; private set; }
    public TabItem? Active => _tabs.FirstOrDefault(t => t.Key == ActiveKey);

    public event Action? Changed;

    public static string KeyOf(NavigationManager nav, string uri)
    {
        var relative = nav.ToBaseRelativePath(nav.ToAbsoluteUri(uri).ToString());
        var hash = relative.IndexOf('#');
        return (hash >= 0 ? relative[..hash] : relative).TrimEnd('/');
    }

    /// <summary>صدا از مسیریاب برای هر ناوبری.</summary>
    public void Open(Microsoft.AspNetCore.Components.RouteData route, string uri)
    {
        var key = KeyOf(nav, uri);
        var existing = _tabs.FirstOrDefault(t => t.Key == key);
        if (existing is null)
        {
            var item = new TabItem
            {
                Key = key,
                PageType = route.PageType,
                Parameters = new Dictionary<string, object?>(route.RouteValues)
            };
            var replace = _replaceKey is null ? null : _tabs.FirstOrDefault(t => t.Key == _replaceKey);
            if (replace is not null)
            {
                item.Version = replace.Version + 1;
                _tabs[_tabs.IndexOf(replace)] = item;
            }
            else
            {
                var index = ActiveKey is null ? -1 : _tabs.FindIndex(t => t.Key == ActiveKey);
                _tabs.Insert(index < 0 ? _tabs.Count : index + 1, item);
                TrimOverflow(item);
            }
        }
        _replaceKey = null;
        ActiveKey = key;
        Changed?.Invoke();
    }

    /// <summary>رفتن به مسیر دیگر داخل همین تب (تب فعلی جایگزین می‌شود).</summary>
    public void Go(string uri)
    {
        _replaceKey = ActiveKey;
        nav.NavigateTo(uri);
    }

    /// <summary>بستن تب فعلی و رفتن به مسیر داده‌شده (مثلاً بعد از حذف رکورد).</summary>
    public void CloseCurrentAndGo(string uri)
    {
        var current = ActiveKey;
        if (current is not null) _tabs.RemoveAll(t => t.Key == current);
        ActiveKey = null;
        nav.NavigateTo(uri);
    }

    public void Activate(string key)
    {
        if (key == ActiveKey) return;
        nav.NavigateTo("/" + key);
    }

    public void Close(string key)
    {
        var index = _tabs.FindIndex(t => t.Key == key);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        if (key == ActiveKey)
        {
            ActiveKey = null;
            var next = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            nav.NavigateTo(next is null ? "/vouchers" : "/" + next.Key);
            return;
        }
        Changed?.Invoke();
    }

    public void CloseOthers(string key)
    {
        _tabs.RemoveAll(t => t.Key != key);
        if (ActiveKey != key) nav.NavigateTo("/" + key);
        else Changed?.Invoke();
    }

    public void SetTitle(string key, string title)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key);
        if (tab is null || tab.Title == title) return;
        tab.Title = title;
        Changed?.Invoke();
    }

    private void TrimOverflow(TabItem keep)
    {
        while (_tabs.Count > MaxTabs)
        {
            var oldest = _tabs.First(t => t != keep && t.Key != ActiveKey);
            _tabs.Remove(oldest);
        }
    }
}

/// <summary>تب صفحه جاری؛ به صفحه‌ها cascade می‌شود.</summary>
public sealed record TabContext(string Key);
