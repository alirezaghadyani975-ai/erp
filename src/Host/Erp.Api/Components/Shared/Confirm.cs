using MudBlazor;

namespace Erp.Api.Components.Shared;

public static class Confirm
{
    /// <summary>پرسش تأیید پیش از حذف. true یعنی کاربر «حذف» را زده است.</summary>
    public static async Task<bool> DeleteAsync(this IDialogService dialogs, string what, string? note = null)
    {
        var result = await dialogs.ShowMessageBoxAsync("تأیید حذف",
            $"{what} حذف شود؟ {note ?? "این کار برگشت‌پذیر نیست."}",
            yesText: "حذف", cancelText: "انصراف");
        return result == true;
    }

    /// <summary>پرسش بله/خیر عمومی.</summary>
    public static async Task<bool> AskAsync(this IDialogService dialogs, string title, string message, string yes, string no = "انصراف")
    {
        var result = await dialogs.ShowMessageBoxAsync(title, message, yesText: yes, cancelText: no);
        return result == true;
    }
}
