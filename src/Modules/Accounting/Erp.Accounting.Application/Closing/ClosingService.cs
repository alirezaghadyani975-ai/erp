using System.Globalization;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Closing;

public sealed record ClosingVoucherDto(long Id, VoucherType Type, int TempNo, int? VoucherNo, VoucherStatus Status, string Date,
    decimal Total, int LineCount, string FiscalYearTitle);

/// <summary>وضعیت بستن یک سال: «باز» (آماده یا با مانع)، «در انتظار تأیید» (اسناد بستن موقت‌اند) یا «بسته».</summary>
public enum ClosingStage : byte { Open = 1, Pending = 2, Closed = 3 }

public sealed record ClosingStatusDto(
    short YearId, string YearTitle, ClosingStage Stage, List<string> Blockers, decimal NetIncome,
    string? NextYearTitle, List<ClosingVoucherDto> Vouchers);

/// <summary>
/// بستن سال مالی در دو قدم:
/// ۱) «صدور اسناد بستن»: سند بستن حساب‌های موقت (سود و زیانی ← سود و زیان جاری) و سند اختتامیه (همه حساب‌های دائم و انتظامی)
///    در سال جاری، و سند افتتاحیه سال بعد (سود و زیان جاری به سود و زیان انباشته منتقل می‌شود). همه موقت‌اند و سال قفل می‌شود؛
///    با «برگشت» همه حذف و سال باز می‌شود.
/// ۲) «قطعی کردن»: اسناد بستن و اختتامیه دائم می‌شوند و سال برای همیشه بسته می‌شود. افتتاحیه سال بعد در همان سال دائم می‌شود.
/// تفصیلی‌ها همان ترکیبی‌اند که در اسناد ثبت شده (برای هر حساب و ترکیب تفصیلی یک ردیف).
/// </summary>
public class ClosingService(IAccountingDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock, IUnitOfWork uow)
{
    public const string Module = "accounting";
    public const string CloseTemporaryDocType = "fin_close_temp";
    public const string CloseDocType = "fin_close";
    public const string OpeningDocType = "fin_opening";

    /// <summary>سود و زیان جاری (طرف بستن حساب‌های موقت) و سود و زیان انباشته (در افتتاحیه سال بعد).</summary>
    public const string CurrentProfitCode = "3801";
    public const string RetainedEarningsCode = "3701";

    public async Task<ClosingStatusDto> StatusAsync(short yearId, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.FiscalView);
        var year = await YearAsync(yearId, ct);
        var vouchers = await ClosingVouchersAsync(year, ct);
        var next = await NextYearAsync(year, ct);
        var stage = year.Status == PeriodStatus.Closed ? ClosingStage.Closed
            : vouchers.Count > 0 ? ClosingStage.Pending : ClosingStage.Open;
        var blockers = stage == ClosingStage.Open ? await BlockersAsync(year, next, ct) : [];
        var chart = await ChartAsync(ct);
        var balances = await BalancesAsync(year.Id, ct);
        var net = balances.Where(b => IsTemporary(chart[b.AccountId])).Sum(b => b.Net);
        return new ClosingStatusDto(year.Id, year.Title, stage, blockers, -net, next?.Title, vouchers);
    }

    /// <summary>قدم اول: صدور اسناد بستن (موقت) و قفل سال.</summary>
    public async Task<ClosingStatusDto> CloseAsync(short yearId, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.FiscalClose);
        var year = await YearAsync(yearId, ct, tracking: true);
        if (year.Status == PeriodStatus.Closed) throw new DomainException($"سال مالی {year.Title} قبلاً بسته شده است.");
        if ((await ClosingVouchersAsync(year, ct)).Count > 0)
            throw new DomainException($"اسناد بستن سال {year.Title} قبلاً صادر شده‌اند؛ قطعی‌شان کنید یا برگردانید.");

        await using var tx = await uow.BeginAsync(ct);
        var next = await NextYearAsync(year, ct) ?? await CreateNextYearAsync(year, ct);
        var blockers = await BlockersAsync(year, next, ct);
        if (blockers.Count > 0) throw new DomainException(string.Join(" ", blockers));

        var chart = await ChartAsync(ct);
        var accounts = await db.Accounts.Where(a => a.Level == AccountLevel.Subsidiary).ToDictionaryAsync(a => a.Id, ct);
        var current = accounts.Values.Single(a => a.Code == CurrentProfitCode);
        var retained = accounts.Values.Single(a => a.Code == RetainedEarningsCode);
        var balances = await BalancesAsync(year.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var tempNo = await db.Vouchers.Where(v => v.FiscalYearId == year.Id).MaxAsync(v => (int?)v.TempNo, ct) ?? 0;
        var created = new List<Voucher>();

        // ۱) بستن حساب‌های موقت به سود و زیان جاری
        var temporary = balances.Where(b => IsTemporary(chart[b.AccountId])).ToList();
        var permanent = balances.Where(b => !IsTemporary(chart[b.AccountId])).ToList();
        if (temporary.Count > 0)
        {
            var v = Voucher.Create(year, ++tempNo, year.EndDate, $"بستن حساب‌های موقت سال {year.Title}", user.UserId, now,
                VoucherType.ClosingTemporary, new SourceDocument(Module, CloseTemporaryDocType, year.Id));
            foreach (var b in temporary) Reverse(v, accounts[b.AccountId], b, "بستن حساب");
            var net = temporary.Sum(b => b.Net); // مثبت = زیان
            if (net != 0)
            {
                v.AddCarryLine(current, Empty, Math.Max(net, 0), Math.Max(-net, 0), net < 0 ? "سود سال" : "زیان سال");
                permanent = Merge(permanent, new Balance(current.Id, Empty, net)); // مانده تازه سود و زیان جاری
            }
            db.Vouchers.Add(v);
            created.Add(v);
        }

        // ۲) اختتامیه و ۳) افتتاحیه سال بعد
        permanent = permanent.Where(b => b.Net != 0).ToList();
        if (permanent.Count > 0)
        {
            var close = Voucher.Create(year, ++tempNo, year.EndDate, $"سند اختتامیه سال {year.Title}", user.UserId, now,
                VoucherType.Closing, new SourceDocument(Module, CloseDocType, year.Id));
            foreach (var b in permanent) Reverse(close, accounts[b.AccountId], b, "اختتامیه");
            db.Vouchers.Add(close);
            created.Add(close);

            // سود و زیان جاری در افتتاحیه به سود و زیان انباشته می‌رود
            var opening = permanent
                .Select(b => b.AccountId == current.Id ? b with { AccountId = retained.Id, Details = Empty } : b)
                .Aggregate(new List<Balance>(), (list, b) => Merge(list, b))
                .Where(b => b.Net != 0).ToList();
            var nextYear = await db.FiscalYears.Include(f => f.Periods).FirstAsync(f => f.Id == next.Id, ct);
            var nextNo = await db.Vouchers.Where(v => v.FiscalYearId == next.Id).MaxAsync(v => (int?)v.TempNo, ct) ?? 0;
            var open = Voucher.Create(nextYear, nextNo + 1, nextYear.StartDate, $"سند افتتاحیه سال {nextYear.Title} (از بستن سال {year.Title})",
                user.UserId, now, VoucherType.Opening, new SourceDocument(Module, OpeningDocType, year.Id));
            foreach (var b in opening)
                open.AddCarryLine(accounts[b.AccountId], b.Details, Math.Max(b.Net, 0), Math.Max(-b.Net, 0),
                    b.AccountId == retained.Id && permanent.Any(p => p.AccountId == current.Id) ? "مانده افتتاحیه با سود و زیان سال قبل" : "مانده افتتاحیه");
            if (open.Lines.Count >= 2)
            {
                db.Vouchers.Add(open);
                created.Add(open);
            }
        }

        // اول پیش‌نویس با ردیف‌ها ذخیره می‌شود، بعد موقت (تریگر دیتابیس سند موقت بدون ردیف تراز را نمی‌پذیرد)
        await db.SaveChangesAsync(ct);
        foreach (var v in created) v.MarkTemporary();
        await db.SaveChangesAsync(ct);
        year.Lock();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await StatusAsync(yearId, ct);
    }

    /// <summary>برگشت از بستن: اسناد بستن و افتتاحیه حذف و سال باز می‌شود (فقط تا وقتی هیچ‌کدام دائم نشده‌اند).</summary>
    public async Task<ClosingStatusDto> UndoAsync(short yearId, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.FiscalClose);
        var year = await YearAsync(yearId, ct, tracking: true);
        if (year.Status == PeriodStatus.Closed)
            throw new DomainException($"سال مالی {year.Title} قطعی بسته شده و برگشت ندارد.");
        var vouchers = await db.Vouchers.Include(v => v.Lines).Where(v => v.SourceModule == Module && v.SourceDocumentId == year.Id
                && (v.SourceDocType == CloseTemporaryDocType || v.SourceDocType == CloseDocType || v.SourceDocType == OpeningDocType)
                && v.Status != VoucherStatus.Void)
            .ToListAsync(ct);
        if (vouchers.Count == 0) throw new DomainException($"سال {year.Title} سند بستنی ندارد.");
        if (vouchers.FirstOrDefault(v => v.Status == VoucherStatus.Permanent) is { } permanent)
            throw new DomainException($"سند {permanent.VoucherNo} ({Title(permanent.Type)}) دائم شده است و بستن سال برگشت ندارد.");

        await using var tx = await uow.BeginAsync(ct);
        foreach (var v in vouchers) db.Vouchers.Remove(v);
        await db.SaveChangesAsync(ct);
        year.Open();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await StatusAsync(yearId, ct);
    }

    /// <summary>قدم دوم: دائم کردن اسناد بستن و اختتامیه (آخرین شماره‌های قطعی سال) و بستن قطعی سال.</summary>
    public async Task<ClosingStatusDto> FinalizeAsync(short yearId, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.FiscalClose);
        var year = await YearAsync(yearId, ct, tracking: true);
        if (year.Status == PeriodStatus.Closed) throw new DomainException($"سال مالی {year.Title} قبلاً بسته شده است.");
        var vouchers = await db.Vouchers.Include(v => v.Lines).Where(v => v.FiscalYearId == year.Id && v.SourceModule == Module
                && (v.SourceDocType == CloseTemporaryDocType || v.SourceDocType == CloseDocType) && v.Status == VoucherStatus.Temporary)
            .OrderBy(v => v.Type).ToListAsync(ct);
        if (vouchers.Count == 0) throw new DomainException("اول اسناد بستن را صادر کنید.");
        var pending = await db.Vouchers.CountAsync(v => v.FiscalYearId == year.Id && v.SourceModule != Module
            && (v.Status == VoucherStatus.Draft || v.Status == VoucherStatus.Temporary), ct);
        if (pending > 0) throw new DomainException($"{pending} سند پیش‌نویس یا موقت در سال هست؛ برگردانید و اول آن‌ها را تعیین تکلیف کنید.");

        await using var tx = await uow.BeginAsync(ct);
        year.Open(); // سند فقط در سال باز دائم می‌شود
        await db.SaveChangesAsync(ct);
        var lastNo = await db.Vouchers.Where(v => v.FiscalYearId == year.Id).MaxAsync(v => v.VoucherNo, ct) ?? 0;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var v in vouchers) v.MarkPermanent(year, ++lastNo, user.UserId, now);
        await db.SaveChangesAsync(ct);
        year.Close();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await StatusAsync(yearId, ct);
    }

    // ------------------------------------------------------------------

    private sealed record Balance(int AccountId, int?[] Details, decimal Net)
    {
        public string Key => AccountId + ":" + string.Join(",", Details);
    }

    private static readonly int?[] Empty = new int?[Account.MaxDetailLevels];

    private static List<Balance> Merge(List<Balance> list, Balance b)
    {
        var i = list.FindIndex(x => x.Key == b.Key);
        if (i < 0) list.Add(b);
        else list[i] = list[i] with { Net = list[i].Net + b.Net };
        return list;
    }

    private static void Reverse(Voucher v, Account account, Balance b, string description) =>
        v.AddCarryLine(account, b.Details, Math.Max(-b.Net, 0), Math.Max(b.Net, 0), description);

    /// <summary>مانده هر حساب و ترکیب تفصیلی از اسناد دائم (بدون اسناد بستن و اختتامیه).</summary>
    private async Task<List<Balance>> BalancesAsync(short yearId, CancellationToken ct)
    {
        var rows = await (from l in db.VoucherLines.AsNoTracking()
                          join v in db.Vouchers.AsNoTracking() on l.VoucherId equals v.Id
                          where v.FiscalYearId == yearId && v.Status == VoucherStatus.Permanent
                                && v.Type != VoucherType.ClosingTemporary && v.Type != VoucherType.Closing
                          group l by new { l.AccountId, l.Detail1Id, l.Detail2Id, l.Detail3Id, l.Detail4Id, l.Detail5Id, l.Detail6Id }
                          into g
                          select new { g.Key, Net = g.Sum(x => x.Debit - x.Credit) })
            .Where(x => x.Net != 0)
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Key.AccountId)
            .Select(r => new Balance(r.Key.AccountId,
                [r.Key.Detail1Id, r.Key.Detail2Id, r.Key.Detail3Id, r.Key.Detail4Id, r.Key.Detail5Id, r.Key.Detail6Id], r.Net))
            .ToList();
    }

    private sealed record ChartRow(int Id, string Code, AccountType? Type);

    private async Task<Dictionary<int, ChartRow>> ChartAsync(CancellationToken ct) =>
        await db.Accounts.AsNoTracking().Where(a => a.Level == AccountLevel.Subsidiary)
            .Select(a => new ChartRow(a.Id, a.Code, a.Type)).ToDictionaryAsync(a => a.Id, ct);

    /// <summary>حساب موقت (سود و زیانی): نوع حساب، و اگر نوع خالی است گروه‌های ۶، ۷ و ۸.</summary>
    private static bool IsTemporary(ChartRow a) =>
        a.Type == AccountType.ProfitAndLoss || (a.Type is null && a.Code[0] is '6' or '7' or '8');

    private async Task<List<string>> BlockersAsync(FiscalYear year, FiscalYear? next, CancellationToken ct)
    {
        var list = new List<string>();
        if (year.Status != PeriodStatus.Open) list.Add($"سال مالی {year.Title} باز نیست (قفل است).");
        var drafts = await db.Vouchers.CountAsync(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Draft, ct);
        var temps = await db.Vouchers.CountAsync(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Temporary, ct);
        if (drafts > 0) list.Add($"{drafts} سند پیش‌نویس در سال هست؛ تکمیل و دائم یا حذفشان کنید.");
        if (temps > 0) list.Add($"{temps} سند موقت در سال هست؛ همه اسناد پیش از بستن باید دائم شوند.");
        foreach (var code in new[] { CurrentProfitCode, RetainedEarningsCode })
            if (!await db.Accounts.AnyAsync(a => a.Code == code && a.Level == AccountLevel.Subsidiary, ct))
                list.Add($"معین {code} ({(code == CurrentProfitCode ? "سود و زیان جاری" : "سود و زیان انباشته")}) در کدینگ نیست.");
        if (next is not null)
        {
            if (next.Status != PeriodStatus.Open) list.Add($"سال مالی بعد ({next.Title}) باز نیست.");
            var opening = await db.Vouchers.Where(v => v.FiscalYearId == next.Id && v.Type == VoucherType.Opening
                    && v.Status != VoucherStatus.Void).Select(v => (int?)v.TempNo).FirstOrDefaultAsync(ct);
            if (opening is int no)
                list.Add($"سال {next.Title} سند افتتاحیه دارد (شماره موقت {no})؛ برای ساختن افتتاحیه از بستن سال، آن را باطل یا حذف کنید.");
        }
        return list;
    }

    private async Task<List<ClosingVoucherDto>> ClosingVouchersAsync(FiscalYear year, CancellationToken ct)
    {
        var rows = await db.Vouchers.AsNoTracking()
            .Where(v => v.SourceModule == Module && v.SourceDocumentId == year.Id && v.Status != VoucherStatus.Void
                        && (v.SourceDocType == CloseTemporaryDocType || v.SourceDocType == CloseDocType || v.SourceDocType == OpeningDocType))
            .Select(v => new { v.Id, v.Type, v.TempNo, v.VoucherNo, v.Status, v.VoucherDate, v.FiscalYearId,
                Total = v.Lines.Sum(l => l.Debit), Count = v.Lines.Count() })
            .ToListAsync(ct);
        var titles = await db.FiscalYears.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        return rows.OrderBy(r => r.Type == VoucherType.Opening ? 9 : (int)r.Type)
            .Select(r => new ClosingVoucherDto(r.Id, r.Type, r.TempNo, r.VoucherNo, r.Status, PersianDate.ToPersian(r.VoucherDate),
                r.Total, r.Count, titles[r.FiscalYearId])).ToList();
    }

    private async Task<FiscalYear> YearAsync(short id, CancellationToken ct, bool tracking = false)
    {
        access.DemandScope(ScopeKind.FiscalYear, id);
        var query = db.FiscalYears.Include(f => f.Periods).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("سال مالی پیدا نشد.");
    }

    private async Task<FiscalYear?> NextYearAsync(FiscalYear year, CancellationToken ct)
    {
        var start = year.EndDate.AddDays(1);
        return await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.StartDate == start, ct);
    }

    private async Task<FiscalYear> CreateNextYearAsync(FiscalYear year, CancellationToken ct)
    {
        var start = year.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var calendar = new PersianCalendar();
        var id = await db.CreateFiscalYearAsync(calendar.GetYear(start), (byte)calendar.GetMonth(start), ct);
        return await db.FiscalYears.AsNoTracking().FirstAsync(f => f.Id == id, ct);
    }

    private static string Title(VoucherType t) => t switch
    {
        VoucherType.ClosingTemporary => "بستن حساب‌های موقت",
        VoucherType.Closing => "اختتامیه",
        VoucherType.Opening => "افتتاحیه",
        _ => "عادی"
    };
}
