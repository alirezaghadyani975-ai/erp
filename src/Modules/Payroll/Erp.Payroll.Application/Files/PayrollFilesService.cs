using System.Globalization;
using System.IO.Compression;
using System.Text;
using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application.Files;

/// <summary>فایل آماده دانلود با هشدار اطلاعات ناقص.</summary>
public sealed record PayrollFileDto(string FileName, byte[] Content, List<string> Warnings);

/// <summary>
/// فایل‌های قانونی یک اجرای محاسبه‌شده:
/// لیست بیمه تأمین اجتماعی (DSKKAR00.DBF سربرگ کارگاه + DSKWOR00.DBF بیمه‌شدگان؛ dBase III با متن ایران سیستم)
/// و فایل مالیات حقوق (WPyyyymm.TXT کارکنان + WHyyyymm.TXT حقوق ریالی؛ UTF-8 با جداکننده کاما) طبق راهنمای نسخه 1.7.0.5.
/// اطلاعات ناقص (شماره بیمه، کد شغل، نام پدر، ...) جلوی ساخت فایل را نمی‌گیرد و فهرست می‌شود.
/// </summary>
public class PayrollFilesService(IPayrollDbContext db, IAccessControl access, TimeProvider clock)
{
    public const string ListNo = "01";
    private const int IranCountry = 103;
    private const int RialCurrency = 84;

    private static readonly DbfField[] WorkshopFields =
    [
        new("DSK_PRATE", 'N', 2), new("DSK_ID", 'C', 14), new("DSK_NAME", 'C', 30), new("DSK_FARM", 'C', 30), new("DSK_ADRS", 'C', 40),
        new("DSK_KIND", 'N', 1), new("DSK_YY", 'N', 2), new("DSK_MM", 'N', 2), new("DSK_LISTNO", 'C', 12), new("DSK_NUM", 'N', 5),
        new("DSK_TDD", 'N', 6), new("DSK_TROOZ", 'N', 12), new("DSK_TMAH", 'N', 12), new("DSK_TMAZ", 'N', 12), new("DSK_TMASH", 'N', 12),
        new("DSK_TTOTL", 'N', 12), new("DSK_TBIME", 'N', 12), new("DSK_TKOSO", 'N', 12), new("DSK_BIC", 'N', 12), new("DSK_RATE", 'N', 5),
        new("DSK_DISC", 'C', 30), new("DSK_SPOUSE", 'N', 12), new("DSK_INC", 'N', 12)
    ];

    private static readonly DbfField[] WorkerFields =
    [
        new("DSW_JOB", 'C', 8), new("DSW_PRATE", 'N', 10), new("DSW_ID", 'C', 10), new("DSW_YY", 'N', 2), new("DSW_MM", 'N', 2),
        new("DSW_LISTNO", 'C', 12), new("DSW_ID1", 'C', 12), new("DSW_FNAME", 'C', 20), new("DSW_LNAME", 'C', 30), new("DSW_DNAME", 'C', 20),
        new("DSW_IDNO", 'C', 15), new("DSW_IDPLC", 'C', 30), new("DSW_IDATE", 'C', 8), new("DSW_SEX", 'C', 3), new("DSW_NAT", 'C', 10),
        new("DSW_OCP", 'C', 50, RightAlign: true), new("DSW_SDATE", 'C', 8), new("DSW_EDATE", 'C', 8), new("DSW_DD", 'N', 2),
        new("DSW_ROOZ", 'N', 12), new("DSW_MAH", 'N', 12), new("DSW_MAZ", 'N', 12), new("DSW_MASH", 'N', 12), new("DSW_TOTL", 'N', 12),
        new("DSW_BIME", 'N', 12), new("MC_M006", 'N', 15), new("PER_NATCOD", 'C', 10), new("DSW_BDATE", 'C', 8), new("MON_PYM", 'C', 10),
        new("DSW_INC", 'N', 12), new("DSW_SPOUSE", 'N', 12)
    ];

    // ---------------- لیست بیمه ----------------

    public async Task<PayrollFileDto> InsuranceListAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var data = await LoadAsync(runId, ct);
        var (run, workshop) = (data.Run, data.Workshop);
        var warnings = new List<string>();
        var cap = data.Param("MIN_WAGE") * data.Param("INS_CAP_FACTOR");
        if (cap == 0) warnings.Add($"MIN_WAGE یا INS_CAP_FACTOR سال {run.Year} تعریف نشده؛ سقف بیمه اعمال نشد.");
        var (start, end) = Period(run);
        var yy = run.Year % 100;

        var workers = new DbfWriter(WorkerFields, 0xC9);
        decimal tdd = 0, trooz = 0, tmah = 0, tmaz = 0, traw = 0, ttotl = 0, tbime = 0, tkoso = 0, tbic = 0, tspouse = 0, tinc = 0;
        var count = 0;
        foreach (var a in run.Attendances.OrderBy(a => data.Employees[a.EmployeeId].PersonnelNo))
        {
            var e = data.Employees[a.EmployeeId];
            if (e.InsuranceStatus == InsuranceStatus.NotInsured) continue;
            var p = data.Persons.GetValueOrDefault(e.PersonId);
            var amounts = data.Amounts(e.Id);
            var b = InsuranceBase.Compute(data.Items, i => amounts.GetValueOrDefault(i.Id), a.InsuranceDays, cap);
            void Need(object? value, string what) { if (value is null or "") warnings.Add($"{e.PersonnelNo} {e.DisplayName}: {what} ندارد."); }
            Need(e.InsuranceNo, "شماره بیمه");
            Need(e.InsuranceJobCode, "کد شغل بیمه (در «مشاغل»)");
            Need(p?.FatherName, "نام پدر");
            Need(p?.BirthDate, "تاریخ تولد");
            Need(p?.IdNumber, "شماره شناسنامه");
            Need(e.NationalId, "کد ملی");

            var bime = data.Sum(e.Id, i => i.Code == PayRunService.InsuranceWorkerCode);
            var spouse = data.Sum(e.Id, i => i.Code == "MARRIAGE_PAY");
            var leave = e.LeaveDate is { } l && l >= start && l <= end ? ShortDate(l) : null;
            workers.Add(
                e.InsuranceJobCode, null, workshop.Code, yy, run.Month, ListNo, e.InsuranceNo, p?.FirstName, p?.LastName, p?.FatherName,
                p?.IdNumber, p?.IssueCity ?? p?.BirthCity, ShortDate(p?.BirthDate), p?.Gender == 2 ? "زن" : "مرد",
                p?.Nationality ?? (p?.IsForeign == true ? "غیرایرانی" : "ایرانی"), e.JobTitle, ShortDate(e.HireDate), leave,
                a.InsuranceDays, b.DailyWage, b.Wage, b.Benefits, b.Subject, b.Total, bime, null, e.NationalId, ShortDate(p?.BirthDate),
                null, b.DailySeniority, spouse);

            count++;
            tdd += a.InsuranceDays; trooz += Math.Round(b.DailyWage, 0); tmah += b.Wage; tmaz += b.Benefits; traw += b.Wage + b.Benefits;
            ttotl += b.Total; tbime += bime; tspouse += spouse; tinc += Math.Round(b.DailySeniority * a.InsuranceDays, 0);
            tkoso += data.Sum(e.Id, i => i.Code == "INS_EMPLOYER");
            tbic += data.Sum(e.Id, i => i.Code == "INS_UNEMP");
        }
        if (count == 0) throw new DomainException("در این اجرا کارمند مشمول بیمه نیست.");

        var header = new DbfWriter(WorkshopFields, 0x7E);
        int.TryParse(workshop.ContractRow, out var contractRow);
        header.Add(contractRow, workshop.Code, workshop.Title, workshop.EmployerName ?? workshop.Title, workshop.Address, 1, yy, run.Month,
            ListNo, count, tdd, trooz, tmah, tmaz, traw, ttotl, tbime, tkoso, tbic, 0,
            run.Description ?? $"لیست بیمه {run.Title}", tspouse, tinc);

        var now = clock.GetLocalNow().DateTime;
        var zip = Zip(("DSKKAR00.DBF", header.ToArray(now)), ("DSKWOR00.DBF", workers.ToArray(now)));
        return new PayrollFileDto($"insurance-{workshop.Code}-{run.Year}{run.Month:00}.zip", zip, warnings);
    }

    // ---------------- فایل مالیات حقوق ----------------

    public async Task<PayrollFileDto> TaxFilesAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var data = await LoadAsync(runId, ct);
        var run = data.Run;
        var warnings = new List<string>();
        var period = $"{run.Year}{run.Month:00}";
        var wp = new StringBuilder();
        var wh = new StringBuilder();
        var earningsWithoutField = new HashSet<string>();

        foreach (var a in run.Attendances.OrderBy(a => data.Employees[a.EmployeeId].PersonnelNo))
        {
            var e = data.Employees[a.EmployeeId];
            var p = data.Persons.GetValueOrDefault(e.PersonId);
            var amounts = data.Amounts(e.Id);
            if (amounts.Count == 0) continue;
            if (string.IsNullOrEmpty(e.NationalId)) { warnings.Add($"{e.PersonnelNo} {e.DisplayName}: کد ملی ندارد؛ در فایل مالیات نیامد."); continue; }
            void Need(object? value, string what) { if (value is null or "") warnings.Add($"{e.PersonnelNo} {e.DisplayName}: {what} ندارد."); }
            Need(p?.FatherName, "نام پدر");
            Need(p?.BirthDate, "تاریخ تولد");
            Need(e.Education, "مدرک تحصیلی (پرونده پرسنلی)");
            Need(e.JobCategory, "رسته شغلی (پرونده پرسنلی)");

            var insured = e.InsuranceStatus != InsuranceStatus.NotInsured;
            var foreign = p?.IsForeign == true;
            wp.Append(Csv(
                foreign ? 2 : 1, e.NationalId, p?.FirstName, p?.LastName, p?.FatherName, LongDate(p?.BirthDate), Digits(p?.IdNumber),
                p?.BirthCity, e.Education, insured ? 2 : 5, insured ? e.InsuranceNo?.PadLeft(10, '0') : null, insured ? "تامین اجتماعی" : null,
                e.TaxExemption, foreign ? null : IranCountry, foreign ? null : IranCountry, null, null, e.JobCategory, e.JobTitle,
                e.EmploymentType, LongDate(e.HireDate), LongDate(e.LeaveDate), null));

            var fields = new decimal[40];
            foreach (var (item, amount) in data.Items.Select(i => (i, amounts.GetValueOrDefault(i.Id))).Where(x => x.Item2 != 0))
            {
                if (item.TaxField is { } f) fields[f] += amount;
                else if (item.Kind == PayItemKind.Earning)
                {
                    fields[29] += amount;
                    earningsWithoutField.Add(item.Title);
                }
            }
            var earnings = data.Sum(e.Id, i => i.Kind == PayItemKind.Earning);
            var deductions = data.Sum(e.Id, i => i.Kind == PayItemKind.Deduction && i.Source != PayItemSource.TaxOffice);
            var row = new object?[39];
            row[0] = e.NationalId; row[1] = 1; row[2] = e.ServiceLocation; row[3] = 1; row[4] = RialCurrency; row[5] = 1;
            row[6] = fields[7]; row[7] = 0; row[8] = 1; row[9] = 0; row[10] = 1;
            for (var f = 12; f <= 39; f++) row[f - 1] = fields[f];
            row[34] = Math.Max(0, earnings - deductions); // ۳۵: خالص پرداختی پیش از مالیات (مالیات را سامانه حساب می‌کند)
            wh.Append(Csv(row));
        }
        foreach (var t in earningsWithoutField)
            warnings.Add($"قلم «{t}» ستون فایل مالیات ندارد و در ستون ۲۹ (سایر غیرمستمر) آمد؛ در «اقلام حقوق» ستونش را تعیین کنید.");
        if (wh.Length == 0) throw new DomainException("در این اجرا کارمندی با حقوق و کد ملی نیست.");

        var utf8 = new UTF8Encoding(false);
        var zip = Zip(($"WP{period}.TXT", utf8.GetBytes(wp.ToString())), ($"WH{period}.TXT", utf8.GetBytes(wh.ToString())));
        return new PayrollFileDto($"tax-{data.Workshop.Code}-{period}.zip", zip, warnings);
    }

    // ---------------- کمکی ----------------

    private sealed record RunData(PayRun Run, WorkshopRef Workshop, Dictionary<int, EmployeeRef> Employees,
        Dictionary<int, PersonInfoRef> Persons, List<PayItem> Items, Dictionary<string, decimal> Parameters)
    {
        public decimal Param(string code) => Parameters.GetValueOrDefault(code);

        public Dictionary<int, decimal> Amounts(int employeeId) =>
            Run.Lines.Where(l => l.EmployeeId == employeeId).ToDictionary(l => l.PayItemId, l => l.Amount);

        public decimal Sum(int employeeId, Func<PayItem, bool> filter)
        {
            var ids = Items.Where(filter).Select(i => i.Id).ToHashSet();
            return Run.Lines.Where(l => l.EmployeeId == employeeId && ids.Contains(l.PayItemId)).Sum(l => l.Amount);
        }
    }

    private async Task<RunData> LoadAsync(int runId, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
        if (run.Status == PayRunStatus.Draft)
            throw new DomainException(run.CalculatedAt is null
                ? "این اجرا هنوز محاسبه نشده است؛ اول «محاسبه» بزنید."
                : "بعد از آخرین محاسبه کارکرد تغییر کرده است؛ اول دوباره «محاسبه» بزنید.");
        var workshop = await db.Workshops.FirstAsync(w => w.Id == run.WorkshopId, ct);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var personIds = employees.Values.Select(e => e.PersonId).ToList();
        var persons = await db.PersonInfos.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var items = await db.PayItems.AsNoTracking().ToListAsync(ct);
        var parameters = await db.YearParams.AsNoTracking().Where(p => p.Year == run.Year).ToDictionaryAsync(p => p.Code, p => p.Value, ct);
        return new RunData(run, workshop, employees, persons, items, parameters);
    }

    private static (DateOnly Start, DateOnly End) Period(PayRun run)
    {
        var start = PersianDate.FromPersian(run.Year, run.Month, 1);
        return (start, start.AddDays(PersianDate.DaysInMonth(run.Year, run.Month) - 1));
    }

    /// <summary>yy/mm/dd شمسی (قالب لیست بیمه).</summary>
    private static string? ShortDate(DateOnly? date) => date is { } d ? PersianDate.ToPersian(d)[2..] : null;

    /// <summary>YYYYMMDD شمسی (قالب فایل مالیات).</summary>
    private static string? LongDate(DateOnly? date) => date is { } d ? PersianDate.ToPersian(d).Replace("/", "") : null;

    private static string? Digits(string? s) => s is null ? null : new string(s.Where(char.IsAsciiDigit).ToArray());

    /// <summary>یک سطر با جداکننده کاما و پایان CRLF؛ کاما و خط تازه داخل متن حذف می‌شود.</summary>
    private static string Csv(params object?[] values) =>
        string.Join(",", values.Select(v => v switch
        {
            null => "",
            decimal d => Math.Round(d, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => (v.ToString() ?? "").Replace(",", " ").Replace("،", " ").Replace("\r", " ").Replace("\n", " ").Trim()
        })) + "\r\n";

    private static byte[] Zip(params (string Name, byte[] Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(content);
            }
        return ms.ToArray();
    }
}
