namespace Erp.Payroll.Application.Files;

/// <summary>
/// تبدیل متن فارسی به کدپیج «ایران سیستم» با شکل حروف (اول/وسط/آخر/تنها) و ترتیب دیداری (معکوس)،
/// همان‌طور که نرم‌افزار لیست بیمه تأمین اجتماعی (DSKKAR00/DSKWOR00) می‌نویسد.
/// ارقام و حروف لاتین ترتیب چپ‌به‌راست خود را نگه می‌دارند.
/// </summary>
public static class IranSystem
{
    // (تنها، آخر، اول، وسط)
    private static readonly Dictionary<char, (byte Iso, byte Fin, byte Ini, byte Med)> Forms = new()
    {
        ['ا'] = (0x90, 0x91, 0x90, 0x91), ['آ'] = (0x8D, 0x8D, 0x8D, 0x8D), ['أ'] = (0x90, 0x91, 0x90, 0x91), ['إ'] = (0x90, 0x91, 0x90, 0x91),
        ['ب'] = (0x92, 0x92, 0x93, 0x93), ['پ'] = (0x94, 0x94, 0x95, 0x95), ['ت'] = (0x96, 0x96, 0x97, 0x97),
        ['ث'] = (0x98, 0x98, 0x99, 0x99), ['ج'] = (0x9A, 0x9A, 0x9B, 0x9B), ['چ'] = (0x9C, 0x9C, 0x9D, 0x9D),
        ['ح'] = (0x9E, 0x9E, 0x9F, 0x9F), ['خ'] = (0xA0, 0xA0, 0xA1, 0xA1), ['د'] = (0xA2, 0xA2, 0xA2, 0xA2),
        ['ذ'] = (0xA3, 0xA3, 0xA3, 0xA3), ['ر'] = (0xA4, 0xA4, 0xA4, 0xA4), ['ز'] = (0xA5, 0xA5, 0xA5, 0xA5),
        ['ژ'] = (0xA6, 0xA6, 0xA6, 0xA6), ['س'] = (0xA7, 0xA7, 0xA8, 0xA8), ['ش'] = (0xA9, 0xA9, 0xAA, 0xAA),
        ['ص'] = (0xAB, 0xAB, 0xAC, 0xAC), ['ض'] = (0xAD, 0xAD, 0xAE, 0xAE), ['ط'] = (0xAF, 0xAF, 0xAF, 0xAF),
        ['ظ'] = (0xE0, 0xE0, 0xE0, 0xE0), ['ع'] = (0xE1, 0xE2, 0xE4, 0xE3), ['غ'] = (0xE5, 0xE6, 0xE8, 0xE7),
        ['ف'] = (0xE9, 0xE9, 0xEA, 0xEA), ['ق'] = (0xEB, 0xEB, 0xEC, 0xEC), ['ک'] = (0xED, 0xED, 0xEE, 0xEE),
        ['گ'] = (0xEF, 0xEF, 0xF0, 0xF0), ['ل'] = (0xF1, 0xF1, 0xF3, 0xF3), ['م'] = (0xF4, 0xF4, 0xF5, 0xF5),
        ['ن'] = (0xF6, 0xF6, 0xF7, 0xF7), ['و'] = (0xF8, 0xF8, 0xF8, 0xF8), ['ؤ'] = (0xF8, 0xF8, 0xF8, 0xF8),
        ['ه'] = (0xF9, 0xF9, 0xFB, 0xFA), ['ة'] = (0xF9, 0xF9, 0xFB, 0xFA),
        ['ی'] = (0xFD, 0xFC, 0xFE, 0xFE), ['ئ'] = (0x8E, 0x8E, 0x8E, 0x8E), ['ء'] = (0x8F, 0x8F, 0x8F, 0x8F)
    };

    /// <summary>حروفی که به حرف بعد نمی‌چسبند.</summary>
    private static readonly HashSet<char> RightJoinOnly = ['ا', 'آ', 'أ', 'إ', 'د', 'ذ', 'ر', 'ز', 'ژ', 'و', 'ؤ', 'ء', 'ئ'];

    private static readonly Dictionary<char, byte> Others = new()
    {
        ['،'] = 0x8A, ['ـ'] = 0x8B, ['؟'] = 0x8C, ['؛'] = 0x3B
    };

    private static bool IsLetter(char c) => Forms.ContainsKey(c);
    private static bool JoinsForward(char c) => IsLetter(c) && !RightJoinOnly.Contains(c);
    private static bool JoinsBackward(char c) => IsLetter(c) && c != 'ء';

    public static byte[] Encode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var s = text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('ى', 'ی');
        var glyphs = new List<(byte B, bool Ltr)>();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '‌') continue; // نیم‌فاصله: فقط جداکننده اتصال
            if (IsLetter(c))
            {
                var prev = PrevLetter(s, i);
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                var joinPrev = prev != '\0' && JoinsForward(prev) && JoinsBackward(c);
                if (c == 'ل' && next is 'ا' or 'آ' or 'أ' or 'إ')
                {
                    glyphs.Add((0xF2, false)); // لا
                    i++;
                    continue;
                }
                var joinNext = JoinsForward(c) && next != '\0' && JoinsBackward(next);
                var f = Forms[c];
                glyphs.Add(((joinPrev, joinNext) switch
                {
                    (false, false) => f.Iso,
                    (true, false) => f.Fin,
                    (false, true) => f.Ini,
                    _ => f.Med
                }, false));
            }
            else if (c is >= '۰' and <= '۹') glyphs.Add(((byte)('0' + (c - '۰')), true));
            else if (Others.TryGetValue(c, out var o)) glyphs.Add((o, false));
            else if (c < 128) glyphs.Add(((byte)c, char.IsAsciiLetterOrDigit(c) || c is '.' or '/' or ':'));
            else glyphs.Add(((byte)' ', false));
        }

        // ترتیب دیداری: کل متن معکوس، و هر رشته چپ‌به‌راست (عدد، لاتین) دوباره به ترتیب خودش
        glyphs.Reverse();
        var result = new List<byte>(glyphs.Count);
        for (var i = 0; i < glyphs.Count;)
        {
            if (!glyphs[i].Ltr) { result.Add(glyphs[i++].B); continue; }
            var j = i;
            while (j < glyphs.Count && glyphs[j].Ltr) j++;
            for (var k = j - 1; k >= i; k--) result.Add(glyphs[k].B);
            i = j;
        }
        return [.. result];
    }

    /// <summary>حرف قبلی (نیم‌فاصله اتصال را قطع می‌کند).</summary>
    private static char PrevLetter(string s, int i) => i > 0 && s[i - 1] != '‌' ? s[i - 1] : '\0';
}
