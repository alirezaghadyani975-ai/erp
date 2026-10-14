using System.Reflection;
using System.Runtime.CompilerServices;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.SharedKernel.Calendar;

namespace Erp.Accounting.Tests;

/// <summary>
/// ساخت داده آزمایشی. موجودیت‌هایی که در برنامه واقعی فقط از دیتابیس می‌آیند
/// (حساب استاندارد، سال مالی) با reflection ساخته می‌شوند.
/// </summary>
internal static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    public static T Set<T>(this T entity, string property, object? value)
    {
        var type = entity!.GetType();
        PropertyInfo? prop = null;
        while (type is not null && (prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)) is null)
            type = type.BaseType;
        prop!.SetValue(entity, value);
        return entity;
    }

    private static T New<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    public static FiscalYear Year1405(PeriodStatus firstPeriod = PeriodStatus.Open)
    {
        var year = (FiscalYear)Activator.CreateInstance(typeof(FiscalYear), nonPublic: true)!;
        year.Set("Id", (short)1).Set("Title", "1405")
            .Set("StartDate", PersianDate.FromPersian(1405, 1, 1))
            .Set("EndDate", PersianDate.FromPersian(1405, 12, 29))
            .Set("Status", PeriodStatus.Open);

        var periods = (List<FiscalPeriod>)typeof(FiscalYear).GetField("_periods", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(year)!;
        for (byte m = 1; m <= 12; m++)
        {
            var p = (FiscalPeriod)Activator.CreateInstance(typeof(FiscalPeriod), nonPublic: true)!;
            var start = PersianDate.FromPersian(1405, m, 1);
            var end = m == 12 ? PersianDate.FromPersian(1405, 12, 29) : PersianDate.FromPersian(1405, m + 1, 1).AddDays(-1);
            p.Set("Id", (int)m).Set("FiscalYearId", (short)1).Set("PeriodNo", m).Set("Title", $"ماه {m}")
                .Set("StartDate", start).Set("EndDate", end)
                .Set("Status", m == 1 ? firstPeriod : PeriodStatus.Open);
            periods.Add(p);
        }
        return year;
    }

    public static DetailType CustomersNatural() =>
        new DetailType("مشتریان حقیقی", OwnerModules.Persons, "01001", "02999", PersonKind.Natural).Set("Id", (short)8);

    public static DetailType CostCenters() =>
        new DetailType("مراکز هزینه", OwnerModules.Manual, "00901", "00920").Set("Id", (short)7);

    public static DetailAccount Customer(string code = "01001") =>
        new DetailAccount(CustomersNatural(), code, "شرکت نمونه", personId: 1).Set("Id", 100);

    public static DetailAccount CostCenter() =>
        new DetailAccount(CostCenters(), "00901", "تولید").Set("Id", 200);

    public static Account Standard(int id, AccountLevel level, string code, string title, int? parentId = null,
        AccountNature? nature = null, AccountType? type = null)
    {
        var a = (Account)Activator.CreateInstance(typeof(Account), nonPublic: true)!;
        return a.Set("Id", id).Set("Level", level).Set("Code", code).Set("Title", title).Set("ParentId", parentId)
            .Set("Nature", nature).Set("Type", type).Set("IsStandard", true).Set("IsActive", true);
    }

    /// <summary>2305 دریافتنی تجاری اشخاص حقیقی؛ سطح ۱ الزامی، مشتری حقیقی</summary>
    public static Account Receivable()
    {
        var a = Standard(3, AccountLevel.Subsidiary, "2305", "دریافتنی تجاری اشخاص حقیقی", 2,
            AccountNature.Debit, AccountType.BalanceSheet);
        a.SetDetailLevel(1, true, [CustomersNatural().Id]);
        return a;
    }

    /// <summary>6101 فروش؛ بدون تفصیلی</summary>
    public static Account Sales() =>
        Standard(12, AccountLevel.Subsidiary, "6101", "فروش", 11, AccountNature.Credit, AccountType.ProfitAndLoss);

    public static Account General23() => Standard(2, AccountLevel.General, "23", "دریافتنی های تجاری", 1);
}
