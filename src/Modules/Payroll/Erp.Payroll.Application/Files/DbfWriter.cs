using System.Globalization;
using System.Text;

namespace Erp.Payroll.Application.Files;

/// <summary>ستون فایل dBase III: نوع C (متن) یا N (عدد صحیح).</summary>
public sealed record DbfField(string Name, char Type, byte Length, bool RightAlign = false);

/// <summary>
/// نوشتن فایل dBase III (نسخه 0x03) مانند فایل‌های لیست بیمه تأمین اجتماعی.
/// متن فارسی با <see cref="IranSystem"/> کدگذاری می‌شود؛ عددها راست‌چین، خالی = فاصله.
/// </summary>
public sealed class DbfWriter(IReadOnlyList<DbfField> fields, byte languageDriver = 0)
{
    private readonly List<byte[]> _records = [];

    public int RecordLength { get; } = 1 + fields.Sum(f => f.Length);

    /// <param name="values">به ترتیب ستون‌ها: string (متن) یا decimal/int/long (عدد) یا null.</param>
    public void Add(params object?[] values)
    {
        if (values.Length != fields.Count) throw new ArgumentException($"تعداد مقادیر ({values.Length}) با ستون‌ها ({fields.Count}) برابر نیست.");
        var rec = new byte[RecordLength];
        rec[0] = 0x20;
        var p = 1;
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            var bytes = Format(f, values[i]);
            Array.Fill(rec, (byte)' ', p, f.Length);
            var start = f.RightAlign || f.Type == 'N' ? p + f.Length - bytes.Length : p;
            Array.Copy(bytes, 0, rec, start, bytes.Length);
            p += f.Length;
        }
        _records.Add(rec);
    }

    public byte[] ToArray(DateTime fileDate)
    {
        using var ms = new MemoryStream();
        var headerLength = (ushort)(32 + 32 * fields.Count + 1);
        var h = new byte[32];
        h[0] = 0x03;
        h[1] = (byte)(fileDate.Year - 1900);
        h[2] = (byte)fileDate.Month;
        h[3] = (byte)fileDate.Day;
        BitConverter.GetBytes(_records.Count).CopyTo(h, 4);
        BitConverter.GetBytes(headerLength).CopyTo(h, 8);
        BitConverter.GetBytes((ushort)RecordLength).CopyTo(h, 10);
        h[29] = languageDriver;
        ms.Write(h);
        foreach (var f in fields)
        {
            var d = new byte[32];
            Encoding.ASCII.GetBytes(f.Name).CopyTo(d, 0);
            d[11] = (byte)f.Type;
            d[16] = f.Length;
            ms.Write(d);
        }
        ms.WriteByte(0x0D);
        foreach (var r in _records) ms.Write(r);
        ms.WriteByte(0x1A);
        return ms.ToArray();
    }

    private static byte[] Format(DbfField f, object? value)
    {
        if (value is null) return [];
        byte[] bytes;
        if (f.Type == 'N')
        {
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            var text = Math.Round(number, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
            if (text.Length > f.Length) throw new InvalidOperationException($"عدد {text} در ستون {f.Name} ({f.Length} رقم) جا نمی‌شود.");
            bytes = Encoding.ASCII.GetBytes(text);
        }
        else
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            bytes = IranSystem.Encode(s);
        }
        // متن ترتیب دیداری دارد: اگر بلندتر از ستون است، از ابتدای منطقی (سمت راست دیداری) نگه داشته می‌شود
        return bytes.Length <= f.Length ? bytes : bytes[^f.Length..];
    }
}
