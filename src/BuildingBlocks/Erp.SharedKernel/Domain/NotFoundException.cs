namespace Erp.SharedKernel.Domain;

/// <summary>رکورد خواسته‌شده پیدا نشد؛ پیام فارسی به کاربر نمایش داده می‌شود.</summary>
public class NotFoundException(string message) : Exception(message);
