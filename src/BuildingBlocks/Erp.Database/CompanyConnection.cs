using System.Data.Common;
using Erp.SharedKernel.Abstractions;
using Microsoft.Data.SqlClient;

namespace Erp.Database;

/// <summary>
/// یک اتصال برای هر درخواست/عملیات به دیتابیس شرکت جاری، مشترک بین DbContext همه ماژول‌ها.
/// هر DbContext در سازنده‌اش با <see cref="Enlist"/> ثبت می‌شود تا در تراکنش مشترک شرکت کند.
/// </summary>
public sealed class CompanyConnection(ICompanyContext company) : IUnitOfWork, IAsyncDisposable
{
    private readonly List<Func<DbTransaction?, CancellationToken, Task>> _participants = [];
    private DbTransaction? _transaction;
    private int _depth;

    public SqlConnection Connection { get; } = new(company.ConnectionString);

    public DbTransaction? CurrentTransaction => _transaction;

    /// <summary>ثبت یک DbContext؛ اگر تراکنشی باز است فوراً به آن می‌پیوندد.</summary>
    public void Enlist(Func<DbTransaction?, CancellationToken, Task> useTransaction, Action<DbTransaction> joinNow)
    {
        _participants.Add(useTransaction);
        if (_transaction is not null) joinNow(_transaction);
    }

    public async Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            _depth++;
            return new Scope(this, outer: false);
        }

        if (Connection.State != System.Data.ConnectionState.Open)
            await Connection.OpenAsync(ct);
        _transaction = await Connection.BeginTransactionAsync(ct);
        _depth = 1;
        foreach (var p in _participants) await p(_transaction, ct);
        return new Scope(this, outer: true);
    }

    private async Task EndAsync(bool commit, CancellationToken ct)
    {
        var tx = _transaction!;
        _transaction = null;
        _depth = 0;
        foreach (var p in _participants) await p(null, ct);
        if (commit) await tx.CommitAsync(ct);
        else await tx.RollbackAsync(ct);
        await tx.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }

    private sealed class Scope(CompanyConnection owner, bool outer) : IUnitOfWorkTransaction
    {
        private bool _done;

        public async Task CommitAsync(CancellationToken ct = default)
        {
            _done = true;
            if (outer) await owner.EndAsync(commit: true, ct);
            else owner._depth--;
        }

        public async ValueTask DisposeAsync()
        {
            if (_done) return;
            _done = true;
            // بدون Commit: کل تراکنش (حتی از سطح درونی) برگردانده می‌شود
            if (owner._transaction is not null) await owner.EndAsync(commit: false, default);
        }
    }
}
