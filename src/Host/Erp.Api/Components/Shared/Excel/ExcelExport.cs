using ClosedXML.Excel;

namespace Erp.Api.Components.Shared.Excel;

/// <summary>نوع ستون اکسل: مبلغ و عدد به‌صورت عدد واقعی (قابل جمع در اکسل)، کد و تاریخ شمسی به‌صورت متن.</summary>
public enum ExcelKind : byte { Text, Amount, Number, Code }

/// <summary>یک ستون خروجی اکسل.</summary>
public sealed record ExcelColumn<T>(string Header, Func<T, object?> Value, ExcelKind Kind = ExcelKind.Text);

/// <summary>فایل آماده دانلود.</summary>
public sealed record ExcelFile(string FileName, byte[] Content, string Title);

/// <summary>
/// ساخت فایل اکسل راست‌به‌چپ از هر فهرست: سطر عنوان، سطرهای توضیح (شرکت، بازه، فیلترها)، سرستون‌ها، داده و جمع.
/// مبالغ عدد واقعی‌اند تا در اکسل قابل جمع و فرمول باشند؛ کدها متن‌اند تا صفرهای اولشان نیفتد.
/// </summary>
public static class ExcelExport
{
    private const string AmountFormat = "#,##0;-#,##0;";

    public static ExcelFile Build<T>(string title, IEnumerable<string?> subtitle, IReadOnlyList<ExcelColumn<T>> columns,
        IEnumerable<T> rows, T? totals = default, string? fileName = null)
    {
        using var book = new XLWorkbook();
        book.Style.Font.FontName = "Tahoma";
        book.Style.Font.FontSize = 10;
        var sheet = book.Worksheets.Add(SheetName(title));
        sheet.RightToLeft = true;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.CenterHorizontally = true;

        var r = 1;
        var titleCell = sheet.Range(r, 1, r, Math.Max(columns.Count, 1)).Merge();
        titleCell.Value = title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 13;
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r++;
        foreach (var line in subtitle.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var range = sheet.Range(r, 1, r, Math.Max(columns.Count, 1)).Merge();
            range.Value = line;
            range.Style.Font.FontColor = XLColor.DimGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            r++;
        }
        r++;

        var headerRow = r;
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(r, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8ECF2");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        var widths = columns.Select(c => (double)Math.Max(8, c.Header.Length + 2)).ToArray();
        var first = r + 1;
        foreach (var row in rows)
        {
            r++;
            Write(sheet, r, columns, row, widths);
        }
        var last = r;
        if (totals is not null)
        {
            r++;
            Write(sheet, r, columns, totals, widths);
            sheet.Row(r).Style.Font.Bold = true;
            sheet.Range(r, 1, r, columns.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        }

        var table = sheet.Range(headerRow, 1, r, columns.Count);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorderColor = XLColor.FromHtml("#C8CDD5");
        if (last >= first)
            sheet.Range(headerRow, 1, last, columns.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        for (var c = 0; c < columns.Count; c++)
            sheet.Column(c + 1).Width = Math.Min(widths[c], 60);

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        // نیم‌فاصله در نام فایل را بعضی مرورگرها به «_» تبدیل می‌کنند
        return new ExcelFile((fileName ?? title).Replace('/', '-').Replace('\u200c', ' ') + ".xlsx", stream.ToArray(), title);
    }

    private static void Write<T>(IXLWorksheet sheet, int r, IReadOnlyList<ExcelColumn<T>> columns, T row, double[] widths)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var value = columns[c].Value(row);
            var cell = sheet.Cell(r, c + 1);
            switch (columns[c].Kind)
            {
                case ExcelKind.Amount or ExcelKind.Number when value is not null:
                    cell.Value = Convert.ToDecimal(value);
                    cell.Style.NumberFormat.Format = columns[c].Kind == ExcelKind.Amount ? AmountFormat : "0.####";
                    widths[c] = Math.Max(widths[c], Convert.ToDecimal(value).ToString("#,0").Length + 3);
                    break;
                default:
                    var text = value?.ToString() ?? "";
                    cell.Value = text;
                    if (columns[c].Kind == ExcelKind.Code)
                    {
                        cell.Style.NumberFormat.Format = "@";
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    widths[c] = Math.Max(widths[c], text.Length * 1.1 + 2);
                    break;
            }
        }
    }

    /// <summary>نام برگه در اکسل حداکثر ۳۱ نویسه و بدون نویسه‌های غیرمجاز است.</summary>
    private static string SheetName(string title)
    {
        var name = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (name.Length == 0) name = "گزارش";
        return name.Length > 31 ? name[..31] : name;
    }
}
