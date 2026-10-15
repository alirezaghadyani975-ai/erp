using MudBlazor;

namespace Erp.Api.Components.Shared.Forms;

/// <summary>یک رکورد در فهرست کنار فرم.</summary>
public sealed record SideItem(string Url, string Title, string? Subtitle = null, string? Amount = null,
    string? Badge = null, Color BadgeColor = Color.Default)
{
    public string SearchText { get; } = Erp.SharedKernel.Text.PersianText.Normalize($"{Title} {Subtitle} {Amount} {Badge}");
}

/// <summary>تنظیمات رابط کاربری کاربر در طول اتصال (مثلاً باز یا بسته بودن فهرست کنار فرم).</summary>
public sealed class UiPrefs
{
    public bool SideListOpen { get; set; } = true;
}
