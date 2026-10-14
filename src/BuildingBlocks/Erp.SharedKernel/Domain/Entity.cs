namespace Erp.SharedKernel.Domain;

/// <summary>پایه همه موجودیت‌ها.</summary>
public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && !IsTransient && Id.Equals(other.Id);

    public override int GetHashCode() => IsTransient ? base.GetHashCode() : Id.GetHashCode();

    private bool IsTransient => EqualityComparer<TId>.Default.Equals(Id, default!);
}
