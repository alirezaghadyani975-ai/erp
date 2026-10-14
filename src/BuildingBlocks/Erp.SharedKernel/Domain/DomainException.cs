namespace Erp.SharedKernel.Domain;

/// <summary>نقض یک قاعده کسب‌وکار. پیام آن مستقیماً به کاربر نمایش داده می‌شود.</summary>
public class DomainException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
