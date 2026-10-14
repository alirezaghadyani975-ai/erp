namespace Erp.SharedKernel.Abstractions;

/// <summary>
/// شرکت جاری درخواست. هر شرکت دیتابیس جداگانه دارد؛ همه ماژول‌ها
/// رشته اتصال را از این‌جا می‌گیرند.
/// </summary>
public interface ICompanyContext
{
    string CompanyKey { get; }
    string ConnectionString { get; }
}
