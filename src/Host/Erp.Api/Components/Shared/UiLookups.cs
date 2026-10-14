using Erp.BaseData.Application.Banks;
using Erp.BaseData.Application.Lookups;
using Erp.SharedKernel.Validation;

namespace Erp.Api.Components.Shared;

/// <summary>
/// حافظه موقت جدول‌های پایه (استان، شهر، بانک، ...) برای فرم‌ها، در طول اتصال کاربر.
/// صفحه‌های تعریف جدول‌های پایه بعد از هر تغییر <see cref="Invalidate"/> را صدا می‌زنند.
/// </summary>
public sealed class UiLookups(Backend backend)
{
    private readonly Dictionary<string, List<LookupItemDto>> _items = new();
    private List<BankDto>? _banks;

    /// <summary>همه اقلام یک فهرست (فعال و غیرفعال؛ غیرفعال‌ها فقط وقتی قبلاً انتخاب شده‌اند نمایش داده می‌شوند).</summary>
    public async Task<List<LookupItemDto>> ItemsAsync(string category)
    {
        if (_items.TryGetValue(category, out var cached)) return cached;
        var r = await backend.Run<LookupService, List<LookupItemDto>>(s => s.GetItemsAsync(category, null, true, default));
        if (!r.Success) return [];
        return _items[category] = r.Value!;
    }

    public async Task<List<BankDto>> BanksAsync()
    {
        if (_banks is not null) return _banks;
        var r = await backend.Run<BankService, List<BankDto>>(s => s.GetAllAsync(true, default));
        return r.Success ? _banks = r.Value! : [];
    }

    public void Invalidate()
    {
        _items.Clear();
        _banks = null;
    }

    /// <summary>بانک از کد داخل شبا، وگرنه از پیش‌شماره کارت (همان قاعده سرور).</summary>
    public static BankDto? DetectBank(IEnumerable<BankDto> banks, string? iban, string? card)
    {
        var list = banks as IList<BankDto> ?? banks.ToList();
        if (IranianIds.IbanBankCode(iban) is { } code && list.FirstOrDefault(b => b.IbanCode == code) is { } byIban)
            return byIban;
        var prefix = IranianIds.CardPrefix(card);
        return prefix is null ? null
            : list.FirstOrDefault(b => b.CardPrefixes is not null && b.CardPrefixes.Split(',').Contains(prefix));
    }

    /// <summary>عنوان کامل قلم درختی (والد / فرزند).</summary>
    public static string Path(IReadOnlyList<LookupItemDto> items, LookupItemDto item)
    {
        var parts = new List<string> { item.Title };
        var parent = item.ParentId;
        for (var guard = 0; parent is int p && guard < 10; guard++)
        {
            var x = items.FirstOrDefault(i => i.Id == p);
            if (x is null || x.CategoryId != item.CategoryId) break;
            parts.Insert(0, x.Title);
            parent = x.ParentId;
        }
        return string.Join(" / ", parts);
    }
}
