using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Application;

public interface IInventoryDbContext
{
    IQueryable<ItemGroup> ItemGroups { get; }
    IQueryable<Item> Items { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: واحدهای سنجش.</summary>
    IQueryable<UnitRef> Units { get; }

    /// <summary>فقط‌خواندنی از حسابداری: معین‌ها (سطح ۳).</summary>
    IQueryable<AccountRef> Accounts { get; }

    /// <summary>کالا در فاکتور خرید یا فروش آمده است؟</summary>
    Task<bool> IsItemUsedAsync(int itemId, CancellationToken ct);

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class UnitRef
{
    public short Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class AccountRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public static class InventoryPermissions
{
    public const string ItemView = "inv.item.view";
    public const string ItemCreate = "inv.item.create";
    public const string ItemEdit = "inv.item.edit";
    public const string ItemDelete = "inv.item.delete";
    public const string Groups = "inv.group.edit";

    public static IPermissionSource List { get; } = new PermissionList("کالا و خدمات")
        .Crud("inv.item", "کالا و خدمات")
        .Add("inv.group", "گروه‌های کالا", ("edit", "تعریف و اصلاح"));
}

/// <param name="SalesAccount">«کد - عنوان» معین فروش.</param>
public sealed record ItemGroupDto(int Id, int? ParentId, string Title, ItemKind Kind,
    int SalesAccountId, string SalesAccount, int SalesReturnAccountId, string SalesReturnAccount,
    int? PurchaseAccountId, string? PurchaseAccount, bool IsActive, int ItemCount);

public sealed record SaveItemGroupRequest(string Title, ItemKind Kind, int SalesAccountId, int SalesReturnAccountId,
    int? PurchaseAccountId, int? ParentId, bool IsActive = true);

public sealed record ItemDto(int Id, string Code, string Title, ItemKind Kind, int GroupId, string GroupTitle,
    short UnitId, string UnitTitle, string? TaxItemId, bool VatExempt, string? Barcode, decimal? SalesPrice, bool IsActive, string? Notes);

/// <param name="Code">خالی = کد بعدی خودکار.</param>
public sealed record SaveItemRequest(string? Code, string Title, int GroupId, short UnitId, string? TaxItemId = null,
    bool VatExempt = false, string? Barcode = null, decimal? SalesPrice = null, bool IsActive = true, string? Notes = null);

public sealed record ItemUnitDto(short Id, string Title);

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        services.AddSingleton(InventoryPermissions.List);
        services.AddScoped<ItemGroupService>();
        services.AddScoped<ItemService>();
        return services;
    }
}
