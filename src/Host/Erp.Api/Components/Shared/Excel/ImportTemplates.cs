using ClosedXML.Excel;
using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;

namespace Erp.Api.Components.Shared.Excel;

/// <summary>فایل‌های نمونه ورود از اکسل (سرستون‌ها، دو سطر مثال و برگه راهنما).</summary>
public static class ImportTemplates
{
    public static readonly string[] VoucherHeaders =
        ["کد حساب", "تفصیلی 1", "تفصیلی 2", "تفصیلی 3", "تفصیلی 4", "تفصیلی 5", "تفصیلی 6", "بدهکار", "بستانکار", "شرح"];

    public static readonly string[] PersonHeaders =
        ["نوع", "نام", "نام خانوادگی", "نام شرکت", "کد ملی یا شناسه ملی", "کد اقتصادی", "موبایل", "تلفن", "ایمیل", "نقش‌ها", "توضیحات"];

    public static ExcelFile Voucher(List<AccountDto> accounts, List<DetailTypeDto> types)
    {
        using var book = NewBook();
        var sheet = Sheet(book, "سند", VoucherHeaders, [1, 2, 3, 4, 5, 6, 7]);
        sheet.Cell(2, 1).Value = "2411"; sheet.Cell(2, 2).Value = "600001"; sheet.Cell(2, 8).Value = 1500000; sheet.Cell(2, 10).Value = "مانده اول دوره مشتری";
        sheet.Cell(3, 1).Value = "3701"; sheet.Cell(3, 9).Value = 1500000; sheet.Cell(3, 10).Value = "سود انباشته";
        sheet.Range(2, 8, 500, 9).Style.NumberFormat.Format = "#,##0";

        var typeTitles = types.ToDictionary(t => t.Id, t => t.Title);
        var guide = book.Worksheets.Add("راهنمای حساب‌ها");
        guide.RightToLeft = true;
        string[] head = ["کد معین", "عنوان", "تفصیلی 1", "تفصیلی 2", "تفصیلی 3", "تفصیلی 4", "تفصیلی 5", "تفصیلی 6"];
        for (var c = 0; c < head.Length; c++) Header(guide.Cell(1, c + 1), head[c]);
        var r = 1;
        foreach (var a in accounts)
        {
            r++;
            guide.Cell(r, 1).SetValue(a.Code);
            guide.Cell(r, 2).Value = a.Title;
            foreach (var level in a.DetailLevels)
                guide.Cell(r, level.LevelNo + 2).Value =
                    string.Join("، ", level.DetailTypeIds.Select(id => typeTitles.GetValueOrDefault(id, "?"))) + (level.IsRequired ? "" : " (اختیاری)");
        }
        guide.Columns(1, 1).Width = 10;
        guide.Columns(2, 2).Width = 40;
        guide.Columns(3, 8).Width = 22;
        guide.SheetView.FreezeRows(1);

        var notes = book.Worksheets.Add("توضیحات");
        notes.RightToLeft = true;
        string[] lines =
        [
            "هر سطر برگه «سند» یک ردیف سند است. ترتیب ستون‌ها مهم نیست ولی نام سرستون‌ها باید همین باشد.",
            "کد حساب: کد معین. تفصیلی‌ها: کد تفصیلی در هر سطح طبق برگه «راهنمای حساب‌ها»؛ اگر کد و نام با هم باشد («600001 علی رضایی») فقط کد خوانده می‌شود.",
            "هر ردیف یا بدهکار دارد یا بستانکار. مبالغ به ریال و بدون اعشار.",
            "اشخاص، صندوق، بانک و سایر تفصیلی‌ها باید از قبل تعریف شده باشند (اشخاص را می‌شود از «ورود اشخاص از اکسل» وارد کرد).",
            "سند به‌صورت پیش‌نویس ثبت می‌شود؛ بعد از بررسی، از فرم سند موقت و دائمش کنید.",
        ];
        for (var i = 0; i < lines.Length; i++) notes.Cell(i + 1, 1).Value = lines[i];
        notes.Column(1).Width = 120;
        return Save(book, "نمونه ورود سند");
    }

    public static ExcelFile Persons(IEnumerable<string> roleTitles)
    {
        using var book = NewBook();
        var sheet = Sheet(book, "اشخاص", PersonHeaders, [5, 6, 7, 8]);
        object[] p1 = ["حقیقی", "علی", "رضایی", "", "0012345679", "", "09121234567", "", "", "مشتری", ""];
        object[] p2 = ["حقوقی", "", "", "شرکت نمونه", "10101234567", "411111111111", "", "02188888888", "", "مشتری، تأمین‌کننده", ""];
        for (var c = 0; c < p1.Length; c++)
        {
            sheet.Cell(2, c + 1).SetValue(p1[c]?.ToString());
            sheet.Cell(3, c + 1).SetValue(p2[c]?.ToString());
        }
        var notes = book.Worksheets.Add("توضیحات");
        notes.RightToLeft = true;
        string[] lines =
        [
            "نوع: «حقیقی» یا «حقوقی». برای حقیقی نام و نام خانوادگی، برای حقوقی نام شرکت لازم است.",
            "کد ملی (حقیقی) یا شناسه ملی (حقوقی) و کد اقتصادی کنترل صحت می‌شوند؛ شخص تکراری (کد ملی یا نام یکسان) ثبت نمی‌شود.",
            "نقش‌ها با «،» یا «,» جدا شوند. نقش‌های تعریف‌شده: " + string.Join("، ", roleTitles),
            "برای هر شخص تفصیلی مشترک خودکار ساخته می‌شود؛ کدش را بعداً در سند افتتاحیه استفاده کنید.",
        ];
        for (var i = 0; i < lines.Length; i++) notes.Cell(i + 1, 1).Value = lines[i];
        notes.Column(1).Width = 120;
        return Save(book, "نمونه ورود اشخاص");
    }

    private static XLWorkbook NewBook()
    {
        var book = new XLWorkbook();
        book.Style.Font.FontName = "Tahoma";
        book.Style.Font.FontSize = 10;
        return book;
    }

    private static IXLWorksheet Sheet(XLWorkbook book, string name, string[] headers, int[] textColumns)
    {
        var sheet = book.Worksheets.Add(name);
        sheet.RightToLeft = true;
        for (var c = 0; c < headers.Length; c++)
        {
            Header(sheet.Cell(1, c + 1), headers[c]);
            sheet.Column(c + 1).Width = Math.Max(14, headers[c].Length + 4);
        }
        foreach (var c in textColumns) sheet.Range(2, c, 2000, c).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);
        return sheet;
    }

    private static void Header(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static ExcelFile Save(XLWorkbook book, string title)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return new ExcelFile(title + ".xlsx", stream.ToArray(), title);
    }
}
