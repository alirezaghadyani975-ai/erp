using System.Globalization;
using System.Text.Json;
using Erp.Database;
using Erp.Security.Application;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.Data.SqlClient;

namespace Erp.Api.Infrastructure;

public sealed record AuditChange(string Field, string FieldTitle, string? Old, string? New);

public sealed record AuditEntryDto(long Id, string At, string Entity, string EntityTitle, long EntityId, char Action,
    string ActionTitle, string? UserName, string? Ip, Guid? OperationId, string? ParentEntity, long? ParentId,
    List<AuditChange> Changes);

/// <param name="Entity">نام موجودیت (مثل Payment)؛ ردیف‌های آن هم می‌آیند.</param>
public sealed record AuditFilter(string? FromDate = null, string? ToDate = null, string? UserName = null, string? Entity = null,
    char? Action = null, string? Term = null, int Take = 300);

/// <summary>
/// خواندن لاگ تغییرات (audit.ChangeLog) دیتابیس شرکت جاری: تاریخچه یک فرم (با ردیف‌هایش) و گزارش کامل با فیلتر.
/// </summary>
public sealed class AuditLogService(CompanyConnection connection, IAccessControl access, IServiceProvider services)
{
    private const string Columns =
        "Id, [At], EntityName, EntityId, [Action], OldValue, NewValue, UserName, Ip, OperationId, ParentEntity, ParentId";

    /// <summary>تاریخچه یک فرم یا تعریف: خودش و ردیف‌ها/زیرمجموعه‌هایش، جدیدترین اول.</summary>
    public async Task<List<AuditEntryDto>> HistoryAsync(string entity, long id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.AuditHistory);
        await DemandCanSeeAsync(entity, id, ct);
        var sql = $"""
            SELECT TOP (2000) {Columns} FROM audit.ChangeLog
            WHERE (EntityName = @e AND EntityId = @id) OR (ParentEntity = @e AND ParentId = @id)
            ORDER BY Id DESC
            """;
        return await QueryAsync(sql, [new SqlParameter("@e", entity), new SqlParameter("@id", id)], ct);
    }

    /// <summary>
    /// تاریخچه فقط برای فرمی که کاربر اجازه دیدن خودش را دارد: خود فرم از سرویسش خوانده می‌شود تا مجوز مشاهده،
    /// محدوده داده (صندوق/بانک/تنخواه/سال مالی) و «فقط اسناد خودش» همان‌جا کنترل شود. اگر فرم حذف شده باشد،
    /// تاریخچه‌اش را فقط کاربر بدون محدودیت داده می‌بیند.
    /// </summary>
    private async Task DemandCanSeeAsync(string entity, long id, CancellationToken ct)
    {
        if (access.IsSystemAdmin) return;
        T S<T>() where T : notnull => services.GetRequiredService<T>();
        try
        {
            switch (entity)
            {
                case "Voucher": await S<Erp.Accounting.Application.Vouchers.VoucherService>().GetAsync(id, ct); break;
                case "Receipt": await S<Erp.Treasury.Application.ReceiptService>().GetAsync(id, ct); break;
                case "Payment": await S<Erp.Treasury.Application.PaymentService>().GetAsync(id, ct); break;
                case "Transfer": await S<Erp.Treasury.Application.TransferService>().GetAsync(id, ct); break;
                case "ImprestSettlement": await S<Erp.Treasury.Application.ImprestSettlementService>().GetAsync(id, ct); break;
                case "ChequeOperation": await S<Erp.Treasury.Application.ChequeOperationService>().GetAsync(id, ct); break;
                case "Guarantee": await S<Erp.Treasury.Application.GuaranteeService>().GetAsync(id, ct); break;
                case "ExpenseInvoice": await S<Erp.Expenses.Application.ExpenseInvoiceService>().GetAsync(id, ct); break;
                case "Person": await S<Erp.BaseData.Application.Persons.PersonService>().GetAsync((int)id, ct); break;
                case "CostCenter": await S<Erp.Costing.Application.Centers.CostCenterService>().GetAsync((int)id, ct); break;
                default:
                    throw new ForbiddenException("تاریخچه این مورد فقط از گزارش «لاگ تغییرات» دیده می‌شود.");
            }
        }
        catch (Erp.SharedKernel.Domain.NotFoundException)
        {
            var limited = access.OwnDocsOnly || Enum.GetValues<ScopeKind>().Any(k => access.Allowed(k) is not null);
            if (limited) throw new ForbiddenException("این فرم حذف شده است و تاریخچه‌اش فقط برای کاربر بدون محدودیت داده نمایش داده می‌شود.");
        }
    }

    public async Task<List<AuditEntryDto>> SearchAsync(AuditFilter f, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.AuditReport);
        var where = new List<string>();
        var ps = new List<SqlParameter> { new("@take", Math.Clamp(f.Take, 1, 5000)) };
        if (PersianDate.TryParse(f.FromDate, out var from))
        {
            where.Add("[At] >= @from");
            ps.Add(new SqlParameter("@from", PersianDate.IranDayStartUtc(from)));
        }
        if (PersianDate.TryParse(f.ToDate, out var to))
        {
            where.Add("[At] < @to");
            ps.Add(new SqlParameter("@to", PersianDate.IranDayStartUtc(to.AddDays(1))));
        }
        if (PersianText.NormalizeOrNull(f.UserName) is { } user)
        {
            where.Add("UserName = @user");
            ps.Add(new SqlParameter("@user", user));
        }
        if (!string.IsNullOrWhiteSpace(f.Entity))
        {
            where.Add("(EntityName = @entity OR ParentEntity = @entity)");
            ps.Add(new SqlParameter("@entity", f.Entity));
        }
        if (f.Action is { } action)
        {
            where.Add("[Action] = @action");
            ps.Add(new SqlParameter("@action", action.ToString()));
        }
        if (PersianText.NormalizeOrNull(f.Term) is { } term)
        {
            where.Add("(OldValue LIKE @term OR NewValue LIKE @term)");
            ps.Add(new SqlParameter("@term", $"%{term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%"));
        }
        var sql = $"SELECT TOP (@take) {Columns} FROM audit.ChangeLog {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY Id DESC";
        return await QueryAsync(sql, ps, ct);
    }

    /// <summary>کاربرانی که در لاگ این شرکت تغییر ثبت کرده‌اند (برای فیلتر).</summary>
    public async Task<List<string>> UsersAsync(CancellationToken ct)
    {
        access.Demand(SecurityPermissions.AuditReport);
        var result = new List<string>();
        await EnsureOpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DISTINCT UserName FROM audit.ChangeLog WHERE UserName IS NOT NULL ORDER BY UserName",
            connection.Connection, (SqlTransaction?)connection.CurrentTransaction);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private sealed record RawEntry(long Id, DateTime At, string Entity, long EntityId, char Action,
        Dictionary<string, JsonElement> Old, Dictionary<string, JsonElement> New, string? UserName, string? Ip,
        Guid? OperationId, string? ParentEntity, long? ParentId);

    private async Task<List<AuditEntryDto>> QueryAsync(string sql, IEnumerable<SqlParameter> parameters, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        var raw = new List<RawEntry>();
        await using (var cmd = new SqlCommand(sql, connection.Connection, (SqlTransaction?)connection.CurrentTransaction))
        {
            cmd.Parameters.AddRange(parameters.ToArray());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                raw.Add(new RawEntry(reader.GetInt64(0), reader.GetDateTime(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4)[0],
                    Parse(reader.IsDBNull(5) ? null : reader.GetString(5)), Parse(reader.IsDBNull(6) ? null : reader.GetString(6)),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        }

        raw = DropRewrittenRows(raw);
        var names = await ResolveNamesAsync(raw, ct);
        return raw.Select(r => new AuditEntryDto(r.Id,
            PersianDate.ToPersianDateTime(r.At) + ":" + PersianDate.ToIranTime(r.At).ToString("ss"),
            r.Entity, AuditLabels.Entity(r.Entity), r.EntityId, r.Action, AuditLabels.Action(r.Action), r.UserName, r.Ip,
            r.OperationId, r.ParentEntity, r.ParentId, Changes(r, names))).ToList();
    }

    /// <summary>
    /// فرم‌ها هنگام اصلاح ردیف‌هایشان را حذف و دوباره درج می‌کنند؛ ردیفی که با همان مقادیر دوباره ساخته شده
    /// تغییر واقعی نیست و از نمایش حذف می‌شود (جفت حذف/ایجاد هم‌محتوا در یک عملیات).
    /// </summary>
    private static List<RawEntry> DropRewrittenRows(List<RawEntry> rows)
    {
        var drop = new HashSet<long>();
        foreach (var op in rows.Where(r => r.OperationId is not null).GroupBy(r => (r.OperationId, r.Entity)))
        {
            var deleted = op.Where(r => r.Action == 'D').ToList();
            foreach (var inserted in op.Where(r => r.Action == 'I'))
            {
                var signature = Signature(inserted.New, inserted);
                var match = deleted.FirstOrDefault(d => !drop.Contains(d.Id) && Signature(d.Old, d) == signature);
                if (match is null) continue;
                drop.Add(match.Id);
                drop.Add(inserted.Id);
            }
        }
        return drop.Count == 0 ? rows : rows.Where(r => !drop.Contains(r.Id)).ToList();
    }

    private static string Signature(Dictionary<string, JsonElement> values, RawEntry e) =>
        string.Join("|", values.Where(kv => kv.Value.ValueKind != JsonValueKind.Null && !IsTechnical(kv.Key, e)
                                            && kv.Key != "CreatedAt" && kv.Key != "UpdatedAt")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value.GetRawText()}"));

    /// <summary>فیلدهای فنی که به کاربر نشان داده نمی‌شوند: شناسه، کلید والد و شناسه ردیف‌ها.</summary>
    private static bool IsTechnical(string field, RawEntry e) =>
        field == "Id" || field.EndsWith("LineId", StringComparison.Ordinal) || field is "SourceEntityId" or "OperationId"
            or "InvoiceId" or "PaymentId" or "ReceiptId" or "SettlementId"
        || (e.ParentEntity is not null && field == e.ParentEntity + "Id");

    /// <summary>جدول عنوان هر فیلد شناسه‌ای (برای نمایش «صندوق مرکزی» به‌جای عدد ۳).</summary>
    private static readonly Dictionary<string, (string Table, string Title)> Lookups = new()
    {
        ["PersonId"] = ("base.Persons", "DisplayName"),
        ["SupplierId"] = ("base.Persons", "DisplayName"),
        ["HolderPersonId"] = ("base.Persons", "DisplayName"),
        ["CashBoxId"] = ("trs.CashBoxes", "Title"),
        ["BankAccountId"] = ("trs.BankAccounts", "Title"),
        ["ImprestFundId"] = ("trs.ImprestFunds", "Title"),
        ["AccountId"] = ("fin.Accounts", "Code + N' - ' + Title"),
        ["CounterAccountId"] = ("fin.Accounts", "Code + N' - ' + Title"),
        ["CostFactorId"] = ("cost.CostFactors", "Title"),
        ["CostCenterId"] = ("cost.CostCenters", "Title"),
        ["BankId"] = ("base.Banks", "Title"),
        ["FiscalYearId"] = ("fin.FiscalYears", "Title"),
        ["DetailTypeId"] = ("fin.DetailTypes", "Title"),
        ["Detail1Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["Detail2Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["Detail3Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["Detail4Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["Detail5Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["Detail6Id"] = ("fin.DetailAccounts", "Code + N' - ' + Title"),
        ["LeafId"] = ("trs.InstrumentLeaves", "SerialNo"),
        ["TypeId"] = ("trs.TransactionTypes", "Title"),
        ["ManagerPersonId"] = ("base.Persons", "DisplayName"),
        ["CenterId"] = ("cost.CostCenters", "Title"),
        ["TargetCenterId"] = ("cost.CostCenters", "Title"),
        ["FromBankAccountId"] = ("trs.BankAccounts", "Title"),
        ["FromCashBoxId"] = ("trs.CashBoxes", "Title"),
    };

    /// <summary>نوع (TypeId) فقط در فرم‌های خزانه نوع دریافت/پرداخت است.</summary>
    private static bool UsesLookup(string field, string entity) =>
        Lookups.ContainsKey(field) && (field != "TypeId" || entity is "Receipt" or "Payment" or "ImprestSettlementLine");

    private async Task<Dictionary<(string Table, long Id), string>> ResolveNamesAsync(List<RawEntry> rows, CancellationToken ct)
    {
        var wanted = new Dictionary<string, HashSet<long>>();
        foreach (var r in rows)
            foreach (var (field, value) in r.Old.Concat(r.New))
                if (UsesLookup(field, r.Entity) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var id))
                {
                    var (table, _) = Lookups[field];
                    if (!wanted.TryGetValue(table, out var set)) wanted[table] = set = [];
                    set.Add(id);
                }

        var result = new Dictionary<(string, long), string>();
        foreach (var (table, ids) in wanted)
        {
            var title = Lookups.Values.First(l => l.Table == table).Title;
            foreach (var chunk in ids.Chunk(500))
            {
                // جدول و ستون از فهرست ثابت بالا می‌آیند (نه ورودی کاربر)؛ شناسه‌ها عددی‌اند
                var sql = $"SELECT Id, {title} FROM {table} WHERE Id IN ({string.Join(",", chunk)})";
                try
                {
                    await using var cmd = new SqlCommand(sql, connection.Connection, (SqlTransaction?)connection.CurrentTransaction);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        if (!reader.IsDBNull(1)) result[(table, Convert.ToInt64(reader.GetValue(0)))] = reader.GetString(1);
                }
                catch (SqlException) { } // جدولی که در این شرکت نیست: عدد نمایش داده می‌شود
            }
        }
        return result;
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (connection.Connection.State != System.Data.ConnectionState.Open) await connection.Connection.OpenAsync(ct);
    }

    private static List<AuditChange> Changes(RawEntry e, Dictionary<(string Table, long Id), string> names)
    {
        string? Format(string field, JsonElement v)
        {
            if (UsesLookup(field, e.Entity) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var id))
                return names.TryGetValue((Lookups[field].Table, id), out var title) ? title : $"#{id} (حذف‌شده)";
            return AuditLabels.Value(field, v);
        }

        return e.Old.Keys.Union(e.New.Keys)
            .Where(k => !IsTechnical(k, e))
            .Select(k => new AuditChange(k, AuditLabels.Field(k),
                e.Old.TryGetValue(k, out var o) ? Format(k, o) : null,
                e.New.TryGetValue(k, out var n) ? Format(k, n) : null))
            .OrderBy(c => AuditLabels.Order(c.Field)).ToList();
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>عنوان فارسی موجودیت‌ها، فیلدها و قالب مقدارها برای نمایش لاگ تغییرات.</summary>
public static class AuditLabels
{
    private static readonly Dictionary<string, string> Entities = new()
    {
        ["Voucher"] = "سند حسابداری", ["VoucherLine"] = "ردیف سند", ["Account"] = "حساب", ["AccountDetailLevel"] = "سطح تفصیلی حساب",
        ["AccountDetailLevelType"] = "گروه مجاز سطح تفصیلی", ["AccountDetailLevelRole"] = "نقش مجاز سطح تفصیلی",
        ["DetailType"] = "گروه تفصیلی", ["DetailAccount"] = "حساب تفصیلی", ["FiscalYear"] = "سال مالی", ["FiscalPeriod"] = "دوره مالی",
        ["PostingTemplate"] = "الگوی صدور سند", ["PostingTemplateLine"] = "ردیف الگوی سند",
        ["Person"] = "شخص", ["PersonRole"] = "نقش شخص", ["PersonAddress"] = "نشانی شخص", ["PersonContact"] = "رابط شخص",
        ["PersonBankAccount"] = "حساب بانکی شخص", ["PersonAttachment"] = "پیوست شخص", ["PersonRoleType"] = "نقش اشخاص",
        ["LookupItem"] = "قلم جدول پایه", ["Bank"] = "بانک", ["VatRate"] = "نرخ مالیات و عوارض",
        ["CostFactor"] = "عامل هزینه", ["CostCenter"] = "مرکز هزینه", ["CostCenterType"] = "نوع مرکز هزینه",
        ["CostCenterAllocation"] = "تسهیم مرکز هزینه",
        ["ExpenseInvoice"] = "شناسایی هزینه", ["ExpenseInvoiceLine"] = "ردیف شناسایی هزینه",
        ["CashBox"] = "صندوق", ["BankAccount"] = "حساب بانکی", ["ImprestFund"] = "تنخواه‌گردان", ["TransactionType"] = "نوع دریافت/پرداخت",
        ["Receipt"] = "دریافت", ["ReceiptLine"] = "ردیف دریافت", ["ReceivedCheque"] = "چک دریافتی",
        ["Payment"] = "پرداخت", ["PaymentLine"] = "ردیف پرداخت", ["IssuedCheque"] = "چک صادره",
        ["Transfer"] = "انتقال وجه", ["ImprestSettlement"] = "صورت‌هزینه تنخواه", ["ImprestSettlementLine"] = "ردیف صورت‌هزینه",
        ["ChequeOperation"] = "عملیات چک", ["ChequeOperationLine"] = "ردیف عملیات چک", ["Guarantee"] = "سند تضمینی",
        ["InstrumentBook"] = "دسته‌چک/دفتر سفته", ["InstrumentLeaf"] = "برگ چک/سفته", ["BankGuarantee"] = "ضمانت‌نامه بانکی",
    };

    private static readonly Dictionary<string, string> Fields = new()
    {
        ["Title"] = "عنوان", ["Code"] = "کد", ["Description"] = "شرح", ["Notes"] = "توضیحات", ["IsActive"] = "فعال",
        ["Amount"] = "مبلغ", ["VatAmount"] = "ارزش افزوده", ["Debit"] = "بدهکار", ["Credit"] = "بستانکار", ["Total"] = "جمع",
        ["Fee"] = "کارمزد", ["DocNo"] = "شماره", ["DocDate"] = "تاریخ", ["VoucherDate"] = "تاریخ سند", ["Date"] = "تاریخ",
        ["DueDate"] = "سررسید", ["TempNo"] = "شماره موقت", ["VoucherNo"] = "شماره دائم", ["Status"] = "وضعیت",
        ["PersonId"] = "شخص", ["SupplierId"] = "تأمین‌کننده", ["TypeId"] = "نوع", ["AccountId"] = "حساب",
        ["CashBoxId"] = "صندوق", ["BankAccountId"] = "حساب بانکی", ["ImprestFundId"] = "تنخواه‌گردان", ["BankId"] = "بانک",
        ["CostFactorId"] = "عامل هزینه", ["CostCenterId"] = "مرکز هزینه", ["RowNo"] = "ردیف", ["SerialNo"] = "شماره چک",
        ["SayadId"] = "شناسه صیادی", ["Instrument"] = "نوع", ["RefNo"] = "شماره پیگیری", ["InvoiceNo"] = "شماره فاکتور",
        ["InvoiceDate"] = "تاریخ فاکتور", ["DisplayName"] = "نام نمایشی", ["FirstName"] = "نام", ["LastName"] = "نام خانوادگی",
        ["CompanyName"] = "نام شرکت", ["NationalId"] = "کد/شناسه ملی", ["EconomicCode"] = "کد اقتصادی", ["Mobile"] = "موبایل",
        ["Phone"] = "تلفن", ["Email"] = "ایمیل", ["Iban"] = "شبا", ["AccountNo"] = "شماره حساب", ["CardNo"] = "شماره کارت",
        ["Branch"] = "شعبه", ["Direction"] = "جهت", ["Kind"] = "نوع", ["Number"] = "شماره", ["Subject"] = "موضوع",
        ["IsReturned"] = "مسترد شده", ["ReturnDate"] = "تاریخ استرداد", ["Operation"] = "عملیات", ["FromStatus"] = "وضعیت قبلی",
        ["Detail1Id"] = "تفصیلی ۱", ["Detail2Id"] = "تفصیلی ۲", ["Detail3Id"] = "تفصیلی ۳", ["Detail4Id"] = "تفصیلی ۴",
        ["Detail5Id"] = "تفصیلی ۵", ["Detail6Id"] = "تفصیلی ۶", ["FiscalYearId"] = "سال مالی", ["Type"] = "نوع",
        ["CreatedBy"] = "ثبت‌کننده", ["CreatedAt"] = "زمان ثبت", ["UpdatedAt"] = "زمان اصلاح", ["ApprovedBy"] = "تأییدکننده",
        ["ApprovedAt"] = "زمان دائم شدن", ["SortOrder"] = "ترتیب", ["ParentId"] = "والد", ["RoleId"] = "نقش",
        ["LineKind"] = "نوع ردیف", ["TaxPercent"] = "درصد مالیات", ["TollPercent"] = "درصد عوارض", ["PersianYear"] = "سال",
        ["FollowUpNo"] = "شماره پیگیری", ["FollowUpDate"] = "تاریخ پیگیری", ["LeafId"] = "برگ", ["MaxAmount"] = "سقف مبلغ",
        ["HolderPersonId"] = "تنخواه‌دار", ["DetailTypeId"] = "گروه تفصیلی", ["SourceDocType"] = "نوع فرم مبدأ",
        ["SourceDocumentId"] = "شناسه فرم مبدأ", ["Percent"] = "درصد", ["Nature"] = "ماهیت", ["Behavior"] = "رفتار",
        ["CounterAccountId"] = "معین طرف حساب", ["Side"] = "طرف", ["IsSystemGenerated"] = "سند خودکار", ["SourceModule"] = "زیرسیستم",
        ["ReceivedChequeId"] = "چک دریافتی", ["IssuedChequeId"] = "چک صادره", ["BankGuaranteeId"] = "ضمانت‌نامه بانکی",
        ["BookId"] = "دفترچه", ["No"] = "شماره", ["VoidReason"] = "علت ابطال", ["VoidDate"] = "تاریخ ابطال",
        ["ReceivedDate"] = "تاریخ دریافت", ["FromNo"] = "از شماره", ["ToNo"] = "تا شماره", ["Series"] = "سری",
        ["Fee"] = "کارمزد", ["FeeFactorId"] = "عامل هزینه کارمزد", ["FeeCenterId"] = "مرکز هزینه کارمزد",
        ["FromKind"] = "نوع مبدأ", ["FromId"] = "مبدأ", ["ToKind"] = "نوع مقصد", ["ToId"] = "مقصد",
        ["DrawerName"] = "صاحب حساب چک", ["IsDefault"] = "پیش‌فرض", ["IsSystem"] = "سیستمی",
        ["UploadedAt"] = "زمان بارگذاری", ["UploadedBy"] = "بارگذاری‌کننده", ["FileName"] = "نام فایل", ["ContentType"] = "نوع فایل",
        ["SizeBytes"] = "حجم فایل (بایت)", ["AccountKind"] = "نوع حساب", ["PersonKind"] = "نوع شخص", ["Gender"] = "جنسیت",
        ["PersonGroupId"] = "گروه شخص", ["CityId"] = "شهر", ["ProvinceId"] = "استان", ["Address"] = "نشانی", ["PostalCode"] = "کد پستی",
        ["IsForeign"] = "تبعه خارجی", ["TaxPayerType"] = "نوع مودی", ["BirthDate"] = "تاریخ تولد", ["FatherName"] = "نام پدر",
        ["PassportNo"] = "شماره گذرنامه", ["MaritalStatus"] = "وضعیت تأهل", ["ForeignNationalCode"] = "کد فراگیر اتباع",
        ["Website"] = "وب‌سایت", ["FullName"] = "نام کامل", ["NationalityId"] = "تابعیت", ["LegalType"] = "نوع شخصیت حقوقی",
        ["RegistrationNo"] = "شماره ثبت", ["RegistrationDate"] = "تاریخ ثبت", ["RegistrationCityId"] = "محل ثبت",
        ["IdNumber"] = "شماره شناسنامه", ["IdIssueCityId"] = "محل صدور", ["BirthCityId"] = "محل تولد",
        ["ContactRoleId"] = "سمت رابط", ["AddressTypeId"] = "نوع نشانی", ["BranchCode"] = "کد شعبه", ["IbanCode"] = "کد بانک در شبا",
        ["CardPrefixes"] = "پیش‌شماره کارت", ["CategoryId"] = "جدول پایه", ["SystemKey"] = "کلید سیستمی", ["OwnerModule"] = "ماژول مالک",
        ["VoucherId"] = "سند حسابداری", ["PeriodNo"] = "شماره دوره", ["StartDate"] = "تاریخ شروع", ["EndDate"] = "تاریخ پایان",
        ["Level"] = "سطح", ["LevelNo"] = "شماره سطح", ["IsRequired"] = "اجباری", ["IsStandard"] = "استاندارد",
        ["CodeFrom"] = "از کد", ["CodeTo"] = "تا کد", ["Digits"] = "تعداد رقم", ["CurrencyId"] = "ارز", ["CurrencyAmount"] = "مبلغ ارزی",
        ["ExchangeRate"] = "نرخ ارز", ["Quantity"] = "مقدار", ["UnitId"] = "واحد", ["CostNature"] = "ماهیت هزینه",
        ["CostFunction"] = "وظیفه هزینه", ["Function"] = "وظیفه", ["VariablePercent"] = "درصد متغیر", ["CenterTypeId"] = "نوع مرکز",
        ["CenterId"] = "مرکز هزینه", ["TargetCenterId"] = "مرکز مقصد", ["AllocationBaseId"] = "مبنای تسهیم",
        ["AbsorptionBaseId"] = "مبنای جذب", ["AllocationOrder"] = "ترتیب تسهیم", ["ManagerPersonId"] = "مسئول مرکز",
        ["FromBankAccountId"] = "از حساب بانکی", ["FromCashBoxId"] = "از صندوق", ["ToStatus"] = "وضعیت جدید",
        ["PayableKind"] = "نوع سند پرداختنی", ["CancelDate"] = "تاریخ ابطال", ["ExpiryDate"] = "تاریخ اعتبار", ["IssueDate"] = "تاریخ صدور",
    };

    /// <summary>عنوان فارسی مقدارهای شمارشی (enum) رایج؛ «فیلد:مقدار» بر «مقدار» مقدم است.</summary>
    private static readonly Dictionary<string, string> EnumTitles = new()
    {
        ["Cash"] = "نقد", ["Bank"] = "بانکی", ["Cheque"] = "چک", ["SpendCheque"] = "خرج چک دریافتی",
        ["InBox"] = "در صندوق", ["InCollection"] = "در جریان وصول", ["Collected"] = "وصول‌شده", ["Bounced"] = "برگشتی",
        ["Legal"] = "حقوقی", ["Returned"] = "عودت‌شده", ["Spent"] = "خرج‌شده",
        ["Issued"] = "صادره", ["Cleared"] = "پاس‌شده", ["Cancelled"] = "باطل",
        ["Direction:Issued"] = "پرداختی", ["Direction:Received"] = "دریافتی",
        ["PromissoryNote"] = "سفته", ["BankGuarantee"] = "ضمانت‌نامه بانکی", ["Other"] = "سایر",
        ["Draft"] = "پیش‌نویس", ["Temporary"] = "موقت", ["Permanent"] = "دائم", ["Void"] = "باطل",
        ["Opening"] = "افتتاحیه", ["Normal"] = "عادی", ["Closing"] = "اختتامیه", ["ClosingTemporary"] = "بستن حساب‌های موقت",
        ["Open"] = "باز", ["Locked"] = "قفل", ["Closed"] = "بسته",
        ["Blank"] = "سفید", ["Used"] = "استفاده‌شده", ["Available"] = "نزد ما", ["Delivered"] = "نزد ذی‌نفع",
        ["ChequeBook"] = "دسته‌چک", ["PromissoryNoteBook"] = "دفتر سفته",
        ["Expense"] = "هزینه", ["Kind:Payment"] = "پرداخت", ["LineKind:Payment"] = "پرداخت به طرف حساب", ["Kind:Receipt"] = "دریافت",
        ["Trade"] = "تجاری", ["Deposit"] = "واگذاری به بانک", ["Collect"] = "وصول", ["Bounce"] = "برگشت",
        ["ReturnToDrawer"] = "عودت به صاحب چک", ["Withdraw"] = "برگشت از واگذاری", ["Clear"] = "پاس", ["Cancel"] = "ابطال",
        ["Individual"] = "حقیقی", ["Legal:Individual"] = "حقیقی",
    };

    private static readonly string[] AmountFields =
        ["Amount", "VatAmount", "Debit", "Credit", "Total", "Fee", "MaxAmount", "CurrencyAmount"];

    public static string Entity(string name) => Entities.GetValueOrDefault(name, name);

    /// <summary>فرم‌ها و تعریف‌های اصلی برای فیلتر گزارش (ردیف‌ها همراه فرمشان می‌آیند).</summary>
    public static readonly string[] Roots =
    [
        "Voucher", "Receipt", "Payment", "Transfer", "ImprestSettlement", "ChequeOperation", "Guarantee", "ExpenseInvoice",
        "Person", "Account", "DetailType", "DetailAccount", "FiscalYear", "PostingTemplate", "CostCenter", "CostFactor",
        "CostCenterType", "CashBox", "BankAccount", "ImprestFund", "TransactionType", "InstrumentBook", "BankGuarantee",
        "Bank", "LookupItem", "PersonRoleType", "VatRate",
    ];

    public static string Field(string name) => Fields.GetValueOrDefault(name, name);

    public static string Action(char action) => action switch
    {
        'I' => "ایجاد",
        'U' => "اصلاح",
        'D' => "حذف",
        _ => "تأیید",
    };

    /// <summary>ترتیب نمایش: عنوان‌ها و مبالغ اول، فیلدهای فنی آخر.</summary>
    public static int Order(string field) => field switch
    {
        "Title" or "DisplayName" or "DocNo" or "RowNo" => 0,
        "DocDate" or "Date" or "VoucherDate" => 1,
        _ when AmountFields.Contains(field) => 2,
        "CreatedBy" or "CreatedAt" or "UpdatedAt" => 9,
        _ => 5,
    };

    public static string? Value(string field, JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return "بله";
            case JsonValueKind.False:
                return "خیر";
            case JsonValueKind.Number:
                return AmountFields.Contains(field) && v.TryGetDecimal(out var d)
                    ? d.ToString("#,0.####", CultureInfo.InvariantCulture)
                    : v.GetRawText();
            case JsonValueKind.String:
                var s = v.GetString()!;
                if (EnumTitles.TryGetValue($"{field}:{s}", out var specific) || EnumTitles.TryGetValue(s, out specific))
                    return specific;
                if (s.Length == 10 && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return PersianDate.ToPersian(date);
                if (s.Length >= 19 && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                    return PersianDate.ToPersianDateTime(dt);
                return s;
            default:
                return v.GetRawText();
        }
    }
}
