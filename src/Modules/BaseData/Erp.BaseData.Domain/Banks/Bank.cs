using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.SharedKernel.Validation;

namespace Erp.BaseData.Domain.Banks;

/// <summary>بانک (base.Banks) با کد بانک در شبا و پیش‌شماره کارت‌ها برای تشخیص خودکار.</summary>
public class Bank : Entity<short>
{
    private Bank() { } // EF

    public Bank(string title, string? ibanCode, string? cardPrefixes) => Update(title, ibanCode, cardPrefixes);

    public string Title { get; private set; } = null!;
    public string? IbanCode { get; private set; }
    public string? CardPrefixes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public short SortOrder { get; private set; } = 100;

    public void Update(string title, string? ibanCode, string? cardPrefixes)
    {
        Title = PersianText.NormalizeOrNull(title) ?? throw new DomainException("نام بانک الزامی است.");
        var code = IranianIds.Digits(ibanCode);
        if (code.Length > 0 && (code.Length != 3 || !code.All(char.IsAsciiDigit)))
            throw new DomainException("کد بانک در شبا سه رقم است (مثلاً 017).");
        IbanCode = code.Length == 0 ? null : code;

        var prefixes = (cardPrefixes ?? "").Split([',', '،', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(IranianIds.Digits).Distinct().ToList();
        if (prefixes.Any(p => p.Length != 6 || !p.All(char.IsAsciiDigit)))
            throw new DomainException("پیش‌شماره کارت شش رقم است (مثلاً 603799)؛ چند مورد را با کاما جدا کنید.");
        CardPrefixes = prefixes.Count == 0 ? null : string.Join(",", prefixes);
    }

    public bool MatchesCard(string? card)
    {
        var digits = IranianIds.Digits(card);
        return digits.Length >= 6 && CardPrefixes is not null && CardPrefixes.Split(',').Contains(digits[..6]);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
