using Microsoft.JSInterop;

namespace Erp.Api.Components.Shared.Forms;

/// <summary>
/// میانبرهای صفحه‌کلید فرم‌ها (در طول اتصال): Ctrl+S ذخیره، F2 اصلاح، Esc انصراف، Alt+N جدید.
/// اسکریپت App.razor کلیدها را می‌فرستد و نوار ابزار تب فعال آن را اجرا می‌کند.
/// </summary>
public sealed class Shortcuts : IDisposable
{
    private DotNetObjectReference<Shortcuts>? _ref;

    public event Func<string, Task>? Pressed;

    public async Task AttachAsync(IJSRuntime js)
    {
        if (_ref is not null) return;
        _ref = DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("erpShortcuts.attach", _ref);
    }

    [JSInvokable]
    public async Task OnKey(string action)
    {
        if (Pressed is { } handler)
            foreach (var h in handler.GetInvocationList().Cast<Func<string, Task>>())
                await h(action);
    }

    public void Dispose() => _ref?.Dispose();
}
