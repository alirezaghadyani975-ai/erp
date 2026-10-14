namespace Erp.SharedKernel.Domain;

/// <summary>
/// ریشه تجمیع. رویدادهای دامنه این‌جا جمع می‌شوند تا زیرسیستم‌ها
/// (انبار، فروش، حقوق و ...) بدون وابستگی مستقیم به هم واکنش نشان دهند.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
