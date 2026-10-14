using Erp.SharedKernel.Text;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.Details;

/// <summary>
/// تفصیلی شناور (fin.DetailAccounts). به هیچ معینی نچسبیده؛ قاعده هر معین تعیین می‌کند کجا مجاز است.
/// تفصیلی‌های یک شخص در نقش‌های مختلف (مشتری، تأمین‌کننده، کارمند) با <see cref="PersonId"/> به هم وصل‌اند.
/// </summary>
public class DetailAccount : AggregateRoot<int>
{
    private DetailAccount() { } // EF

    public DetailAccount(DetailType type, string code, string title,
        int? personId = null, string? sourceModule = null, long? sourceEntityId = null)
    {
        if (!type.IsActive)
            throw new DomainException($"نوع تفصیلی «{type.Title}» غیرفعال است.");
        code = code.Trim();
        if (!type.IsInRange(code))
            throw new DomainException($"کد {code} خارج از بازه «{type.Title}» ({type.CodeFrom} تا {type.CodeTo}) است.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان تفصیلی الزامی است.");
        if (type.OwnerModule == OwnerModules.Persons && personId is null)
            throw new DomainException($"تفصیلی «{type.Title}» فقط برای یک شخص ساخته می‌شود.");

        DetailTypeId = type.Id;
        Code = code;
        Title = PersianText.Normalize(title);
        PersonId = personId;
        SourceModule = sourceModule ?? type.OwnerModule;
        SourceEntityId = sourceEntityId;
    }

    public short DetailTypeId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public int? PersonId { get; private set; }
    public string? SourceModule { get; private set; }
    public long? SourceEntityId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Rename(string title) =>
        Title = string.IsNullOrWhiteSpace(title) ? throw new DomainException("عنوان الزامی است.") : PersianText.Normalize(title);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
