using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.Posting;

/// <summary>طرف ردیف الگو.</summary>
public enum PostingSide : byte
{
    Debit = 1,
    Credit = 2
}

/// <summary>قاعده‌های تعیین معین وقتی معین ثابت نیست.</summary>
public static class AccountRules
{
    /// <summary>معین هزینه از روی گروه عامل هزینه (کل) و وظیفه مرکز هزینه (معین).</summary>
    public const string Cost = "cost";

    /// <summary>معین را خود فرم می‌دهد (مثلاً از نوع دریافت و پرداخت).</summary>
    public const string Source = "source";

    public static string Title(string rule) => rule switch
    {
        Cost => "از روی گروه عامل و وظیفه مرکز هزینه",
        Source => "از فرم (مثلاً معین نوع دریافت یا پرداخت)",
        _ => rule
    };
}

/// <summary>
/// الگوی صدور سند یک نوع سند منبع (fin.PostingTemplates)، مثل «شناسایی هزینه».
/// ساختار ردیف‌ها (کلیدها و طرف‌ها) را زیرسیستم تعیین می‌کند؛ کاربر فقط معین ردیف‌های ثابت را عوض می‌کند.
/// </summary>
public class PostingTemplate : AggregateRoot<int>
{
    private readonly List<PostingTemplateLine> _lines = [];

    private PostingTemplate() { } // EF

    public string SourceDocType { get; private set; } = null!;
    public string? SourceModule { get; private set; }
    public string Title { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<PostingTemplateLine> Lines => _lines.AsReadOnly();

    public PostingTemplateLine Line(string key) =>
        _lines.FirstOrDefault(l => l.LineKey == key)
        ?? throw new DomainException($"ردیف «{key}» در الگوی سند «{Title}» تعریف نشده است.");

    public void ChangeAccount(int lineId, Account account)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new DomainException("ردیف الگو پیدا نشد.");
        if (line.AccountRule is not null)
            throw new DomainException($"معین ردیف «{line.Title}» با قاعده تعیین می‌شود و ثابت نیست.");
        if (account.Level != AccountLevel.Subsidiary)
            throw new DomainException($"«{account.Code} - {account.Title}» معین نیست؛ در الگو فقط معین انتخاب می‌شود.");
        if (!account.IsActive)
            throw new DomainException($"حساب «{account.Code} - {account.Title}» غیرفعال است.");
        line.AccountId = account.Id;
    }
}

/// <summary>ردیف الگو (fin.PostingTemplateLines).</summary>
public class PostingTemplateLine
{
    private PostingTemplateLine() { } // EF

    public int Id { get; private set; }
    public int TemplateId { get; private set; }
    public string LineKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public PostingSide Side { get; private set; }

    /// <summary>معین ثابت؛ خالی وقتی معین با <see cref="AccountRule"/> تعیین می‌شود.</summary>
    public int? AccountId { get; internal set; }
    public string? AccountRule { get; private set; }

    /// <summary>توضیح تفصیلی‌هایی که سند منبع برای این ردیف می‌دهد (برای نمایش به کاربر).</summary>
    public string? DetailHint { get; private set; }
    public byte SortOrder { get; private set; }
}
