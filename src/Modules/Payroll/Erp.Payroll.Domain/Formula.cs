using System.Globalization;
using Erp.SharedKernel.Domain;

namespace Erp.Payroll.Domain;

/// <summary>
/// فرمول اقلام حقوق: عدد، متغیر (کد با حروف بزرگ لاتین)، + - * /، پرانتز، مقایسه (&lt; &lt;= &gt; &gt;= = &lt;&gt; نتیجه ۱ یا ۰)
/// و تابع‌های MIN، MAX، ROUND(x, n)، FLOOR، CEILING، ABS، IF(شرط, بله, نه). بدون اجرای کد؛ فقط درخت محاسبه.
/// </summary>
public sealed class Formula
{
    private static readonly Dictionary<string, (int Min, int Max)> Functions = new()
    {
        ["MIN"] = (1, 20), ["MAX"] = (1, 20), ["ROUND"] = (1, 2), ["FLOOR"] = (1, 1),
        ["CEILING"] = (1, 1), ["ABS"] = (1, 1), ["IF"] = (3, 3)
    };

    private readonly Node _root;

    private Formula(string text, Node root, IReadOnlySet<string> variables)
    {
        Text = text;
        _root = root;
        Variables = variables;
    }

    public string Text { get; }

    /// <summary>متغیرهایی که فرمول لازم دارد.</summary>
    public IReadOnlySet<string> Variables { get; }

    /// <summary>متن را تجزیه می‌کند؛ خطای نگارشی = DomainException با پیام فارسی.</summary>
    public static Formula Parse(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) throw new DomainException("فرمول خالی است.");
        if (normalized.Length > 1000) throw new DomainException("فرمول حداکثر ۱۰۰۰ نویسه است.");
        var parser = new Parser(Tokenize(normalized));
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        var vars = new HashSet<string>(StringComparer.Ordinal);
        root.Collect(vars);
        return new Formula(normalized, root, vars);
    }

    /// <summary>محاسبه با مقدار متغیرها؛ متغیر ناشناخته یا تقسیم بر صفر = DomainException.</summary>
    public decimal Evaluate(Func<string, decimal?> variable)
    {
        try { return _root.Eval(variable); }
        catch (OverflowException) { throw new DomainException($"نتیجه فرمول «{Text}» بیش از حد بزرگ است."); }
    }

    /// <summary>ارقام فارسی/عربی به لاتین، حروف به بزرگ، فاصله‌های اضافه حذف.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var chars = text.Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            '٫' => '.',
            '،' => ',',
            '\r' or '\n' or '\t' => ' ',
            _ => char.ToUpperInvariant(c)
        });
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---------------- tokenizer ----------------

    private enum T { Number, Name, Op, LParen, RParen, Comma, End }

    private readonly record struct Token(T Kind, string Text, int Pos);

    private static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == ' ') { i++; continue; }
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                var start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
                list.Add(new Token(T.Number, s[start..i], start));
                continue;
            }
            if (char.IsAsciiLetterUpper(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsAsciiLetterUpper(s[i]) || char.IsAsciiDigit(s[i]) || s[i] == '_')) i++;
                list.Add(new Token(T.Name, s[start..i], start));
                continue;
            }
            switch (c)
            {
                case '(': list.Add(new Token(T.LParen, "(", i)); i++; continue;
                case ')': list.Add(new Token(T.RParen, ")", i)); i++; continue;
                case ',': list.Add(new Token(T.Comma, ",", i)); i++; continue;
                case '+' or '-' or '*' or '/' or '=': list.Add(new Token(T.Op, c.ToString(), i)); i++; continue;
                case '<' or '>':
                    if (i + 1 < s.Length && (s[i + 1] == '=' || (c == '<' && s[i + 1] == '>')))
                    {
                        list.Add(new Token(T.Op, s.Substring(i, 2), i));
                        i += 2;
                    }
                    else { list.Add(new Token(T.Op, c.ToString(), i)); i++; }
                    continue;
            }
            throw new DomainException($"نویسه «{c}» در فرمول مجاز نیست (جایگاه {i + 1}).");
        }
        list.Add(new Token(T.End, "", s.Length));
        return list;
    }

    // ---------------- parser ----------------

    private sealed class Parser(List<Token> tokens)
    {
        private int _i;
        private Token Current => tokens[_i];

        public void ExpectEnd()
        {
            if (Current.Kind != T.End) throw Error($"«{Current.Text}» اضافه است");
        }

        // comparison := additive (op additive)?
        public Node ParseExpression()
        {
            var left = ParseAdditive();
            if (Current.Kind == T.Op && Current.Text is "<" or "<=" or ">" or ">=" or "=" or "<>")
            {
                var op = tokens[_i++].Text;
                left = new Binary(op, left, ParseAdditive());
            }
            return left;
        }

        private Node ParseAdditive()
        {
            var left = ParseTerm();
            while (Current.Kind == T.Op && Current.Text is "+" or "-")
            {
                var op = tokens[_i++].Text;
                left = new Binary(op, left, ParseTerm());
            }
            return left;
        }

        private Node ParseTerm()
        {
            var left = ParseUnary();
            while (Current.Kind == T.Op && Current.Text is "*" or "/")
            {
                var op = tokens[_i++].Text;
                left = new Binary(op, left, ParseUnary());
            }
            return left;
        }

        private Node ParseUnary()
        {
            if (Current.Kind == T.Op && Current.Text is "-" or "+")
            {
                var op = tokens[_i++].Text;
                var operand = ParseUnary();
                return op == "-" ? new Negate(operand) : operand;
            }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var t = Current;
            switch (t.Kind)
            {
                case T.Number:
                    _i++;
                    if (!decimal.TryParse(t.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
                        throw Error($"عدد «{t.Text}» معتبر نیست");
                    return new Constant(v);
                case T.LParen:
                    _i++;
                    var inner = ParseExpression();
                    Expect(T.RParen, "«)» جا افتاده");
                    return inner;
                case T.Name:
                    _i++;
                    if (Current.Kind != T.LParen) return new Variable(t.Text);
                    if (!Functions.TryGetValue(t.Text, out var arity)) throw Error($"تابع «{t.Text}» شناخته نیست");
                    _i++;
                    var args = new List<Node>();
                    if (Current.Kind != T.RParen)
                    {
                        args.Add(ParseExpression());
                        while (Current.Kind == T.Comma) { _i++; args.Add(ParseExpression()); }
                    }
                    Expect(T.RParen, $"«)» تابع {t.Text} جا افتاده");
                    if (args.Count < arity.Min || args.Count > arity.Max)
                        throw Error($"تعداد ورودی‌های تابع {t.Text} درست نیست");
                    return new Call(t.Text, args);
                case T.End:
                    throw Error("فرمول ناتمام است");
                default:
                    throw Error($"«{t.Text}» در این جا انتظار نمی‌رفت");
            }
        }

        private void Expect(T kind, string message)
        {
            if (Current.Kind != kind) throw Error(message);
            _i++;
        }

        private DomainException Error(string message) => new($"فرمول نادرست است: {message} (جایگاه {Current.Pos + 1}).");
    }

    // ---------------- tree ----------------

    private abstract class Node
    {
        public abstract decimal Eval(Func<string, decimal?> v);
        public virtual void Collect(HashSet<string> vars) { }
    }

    private sealed class Constant(decimal value) : Node
    {
        public override decimal Eval(Func<string, decimal?> v) => value;
    }

    private sealed class Variable(string name) : Node
    {
        public override decimal Eval(Func<string, decimal?> v) =>
            v(name) ?? throw new DomainException($"متغیر «{name}» در فرمول تعریف نشده است.");

        public override void Collect(HashSet<string> vars) => vars.Add(name);
    }

    private sealed class Negate(Node operand) : Node
    {
        public override decimal Eval(Func<string, decimal?> v) => -operand.Eval(v);
        public override void Collect(HashSet<string> vars) => operand.Collect(vars);
    }

    private sealed class Binary(string op, Node left, Node right) : Node
    {
        public override decimal Eval(Func<string, decimal?> v)
        {
            var a = left.Eval(v);
            var b = right.Eval(v);
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? throw new DomainException("تقسیم بر صفر در فرمول.") : a / b,
                "<" => a < b ? 1 : 0,
                "<=" => a <= b ? 1 : 0,
                ">" => a > b ? 1 : 0,
                ">=" => a >= b ? 1 : 0,
                "=" => a == b ? 1 : 0,
                _ => a != b ? 1 : 0
            };
        }

        public override void Collect(HashSet<string> vars)
        {
            left.Collect(vars);
            right.Collect(vars);
        }
    }

    private sealed class Call(string name, List<Node> args) : Node
    {
        public override decimal Eval(Func<string, decimal?> v)
        {
            switch (name)
            {
                case "IF":
                    return args[0].Eval(v) != 0 ? args[1].Eval(v) : args[2].Eval(v);
                case "ROUND":
                    var digits = args.Count == 2 ? args[1].Eval(v) : 0;
                    if (digits is < -10 or > 10 || digits != decimal.Truncate(digits))
                        throw new DomainException("رقم گرد کردن در ROUND باید عدد صحیح بین ‎-10 و 10 باشد.");
                    var x = args[0].Eval(v);
                    if (digits >= 0) return Math.Round(x, (int)digits, MidpointRounding.AwayFromZero);
                    var scale = (decimal)Math.Pow(10, (double)-digits);
                    return Math.Round(x / scale, 0, MidpointRounding.AwayFromZero) * scale;
            }
            var values = args.Select(a => a.Eval(v)).ToList();
            return name switch
            {
                "MIN" => values.Min(),
                "MAX" => values.Max(),
                "FLOOR" => Math.Floor(values[0]),
                "CEILING" => Math.Ceiling(values[0]),
                _ => Math.Abs(values[0])
            };
        }

        public override void Collect(HashSet<string> vars)
        {
            foreach (var a in args) a.Collect(vars);
        }
    }
}

/// <summary>ترتیب محاسبه اقلام فرمولی: هر قلم بعد از اقلامی که در فرمولش آمده‌اند؛ دور = خطا.</summary>
public static class FormulaOrder
{
    public static List<string> Sort(IReadOnlyDictionary<string, Formula> formulas)
    {
        var result = new List<string>();
        var state = new Dictionary<string, int>(); // 1 در حال بررسی، 2 تمام
        foreach (var code in formulas.Keys.Order(StringComparer.Ordinal)) Visit(code, []);
        return result;

        void Visit(string code, List<string> path)
        {
            if (state.TryGetValue(code, out var s))
            {
                if (s == 2) return;
                var cycle = path.SkipWhile(p => p != code).Append(code);
                throw new DomainException($"فرمول اقلام دور دارد: {string.Join(" ← ", cycle)}");
            }
            state[code] = 1;
            path.Add(code);
            foreach (var dep in formulas[code].Variables.Where(formulas.ContainsKey).Order(StringComparer.Ordinal)) Visit(dep, path);
            path.RemoveAt(path.Count - 1);
            state[code] = 2;
            result.Add(code);
        }
    }
}
