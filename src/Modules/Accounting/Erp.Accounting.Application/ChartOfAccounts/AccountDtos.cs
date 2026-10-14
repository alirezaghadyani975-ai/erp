using Erp.Accounting.Domain.ChartOfAccounts;

namespace Erp.Accounting.Application.ChartOfAccounts;

public sealed record AccountNodeDto(
    int Id, string Code, string Title, AccountLevel Level, int? ParentId,
    AccountNature? Nature, AccountType? Type, bool IsStandard, bool IsActive, List<AccountNodeDto> Children);

/// <param name="RoleIds">نقش‌هایی که فهرست اشخاص این سطح را فیلتر می‌کنند (فیلتر نرم)؛ خالی = همه.</param>
public sealed record DetailLevelDto(int LevelNo, bool IsRequired, List<short> DetailTypeIds, List<short> RoleIds);

public sealed record AccountDto(
    int Id, string Code, string Title, AccountLevel Level, int? ParentId,
    AccountNature? Nature, AccountType? Type, bool IsStandard, bool IsActive, List<DetailLevelDto> DetailLevels);

/// <summary>حساب اختصاصی شرکت (کل یا معین). برای معین، ماهیت لازم است و نوع اگر خالی باشد از گروه گرفته می‌شود.</summary>
public sealed record CreateAccountRequest(int ParentId, string Code, string Title, AccountNature? Nature, AccountType? Type);

public sealed record UpdateAccountRequest(string Title, bool IsActive);

public sealed record SetDetailLevelRequest(bool IsRequired, List<short> DetailTypeIds, List<short>? RoleIds = null);
