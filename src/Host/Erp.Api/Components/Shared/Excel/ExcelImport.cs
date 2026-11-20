using System.Globalization;
using ClosedXML.Excel;
using Erp.SharedKernel.Text;

namespace Erp.Api.Components.Shared.Excel;

/// <summary>یک سطر داده خوانده‌شده از اکسل با شماره سطرش در فایل (برای پیام خطا).</summary>
public sealed class ExcelRow(int rowNumber, Dictionary<string, string?> cells)
{
    public int RowNumber { get; } = rowNumber;

    /// <summary>متن سلول (نرمال‌شده: ی/ک عربی، ارقام فارسی) یا null.</summary>
    public string? Text(string header) => cells.GetValueOrDefault(ExcelImport.Key(header)) is { Length: > 0 } v ? v : null;

    /// <summary>عدد سلول (با ارقام فارسی و جداکننده هزارگان)؛ خالی = null، نامعتبر = خطا.</summary>
    public decimal? Number(string header)
    {
        var text = Text(header);
        if (text is null) return null;
        var clean = text.Replace(",", "").Replace("٬", "").Replace(" ", "").Replace("٫", ".");
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new FormatException($"سطر {RowNumber}، ستون «{header}»: «{text}» عدد نیست.");
    }

    /// <summary>کد اول سلول؛ «600001 علی رضایی» ← «600001» (برای وقتی کد و نام با هم آمده‌اند).</summary>
    public string? Code(string header) => Text(header)?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
}

/// <summary>
/// خواندن جدول از اولین برگه اکسل: سطر سرستون با پیدا شدن ستون‌های لازم شناخته می‌شود (ترتیب ستون‌ها مهم نیست و
/// سطرهای عنوان بالای جدول نادیده گرفته می‌شوند). سطرهای کاملاً خالی رد می‌شوند.
/// </summary>
public static class ExcelImport
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static List<ExcelRow> Read(Stream stream, IReadOnlyCollection<string> requiredHeaders)
    {
        XLWorkbook book;
        try { book = new XLWorkbook(stream); }
        catch (Exception) { throw new FormatException("فایل اکسل (xlsx) معتبر نیست."); }
        using (book)
        {
            var sheet = book.Worksheets.First();
            var used = sheet.RangeUsed() ?? throw new FormatException("برگه اول فایل خالی است.");
            var required = requiredHeaders.Select(Key).ToHashSet();

            int headerRow = 0;
            Dictionary<int, string> columns = [];
            foreach (var row in used.Rows().Take(30))
            {
                var map = row.Cells().Where(c => !c.IsEmpty())
                    .ToDictionary(c => c.Address.ColumnNumber, c => Key(c.GetFormattedString()));
                if (required.All(h => map.ContainsValue(h)))
                {
                    headerRow = row.RowNumber();
                    columns = map;
                    break;
                }
            }
            if (headerRow == 0)
                throw new FormatException($"سطر سرستون پیدا نشد؛ ستون‌های لازم: {string.Join("، ", requiredHeaders)}. از فایل نمونه استفاده کنید.");

            var result = new List<ExcelRow>();
            var lastRow = used.LastRow().RowNumber();
            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                var cells = new Dictionary<string, string?>();
                foreach (var (col, key) in columns)
                {
                    var cell = sheet.Cell(r, col);
                    cells[key] = cell.IsEmpty() ? null : Value(cell);
                }
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                result.Add(new ExcelRow(r, cells));
            }
            return result;
        }
    }

    /// <summary>کلید مقایسه سرستون: بدون فاصله و نیم‌فاصله، با ی/ک و ارقام استاندارد.</summary>
    public static string Key(string header) =>
        PersianText.Normalize(header).Replace(" ", "").Replace("‌", "").Replace("_", "").Replace("-", "");

    private static string? Value(IXLCell cell)
    {
        string text;
        if (cell.DataType == XLDataType.Number)
            text = cell.GetDouble().ToString("0.####", CultureInfo.InvariantCulture);
        else
            text = cell.GetFormattedString();
        var normalized = PersianText.Normalize(text).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
