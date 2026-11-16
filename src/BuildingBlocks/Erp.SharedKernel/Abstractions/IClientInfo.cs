namespace Erp.SharedKernel.Abstractions;

/// <summary>اطلاعات اتصال کاربر جاری (IP) برای لاگ امنیتی و لاگ تغییرات.</summary>
public interface IClientInfo
{
    string? Ip { get; }
}
