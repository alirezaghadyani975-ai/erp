using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Erp.SharedKernel.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Database;

/// <summary>شناسه یک عملیات کاربر (یک scope)؛ همه تغییرات یک ذخیره با هم دیده می‌شوند.</summary>
public sealed class AuditScope
{
    public Guid OperationId { get; } = Guid.NewGuid();
}

/// <summary>
/// لاگ تغییرات: هر درج/اصلاح/حذف موجودیت‌های دیتابیس شرکت در audit.ChangeLog ثبت می‌شود
/// (اصلاح فقط با فیلدهای تغییرکرده، مقدار قبلی و جدید به‌صورت JSON). ردیف‌ها و زیرمجموعه‌ها با
/// ParentEntity/ParentId به فرم اصلی (ریشه تجمیع) وصل می‌شوند تا تاریخچه فرم کامل باشد.
/// لاگ در همان تراکنش ذخیره نوشته می‌شود؛ اگر ذخیره برگردد، لاگ هم برمی‌گردد.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser user, AuditScope scope, IServiceProvider services) : SaveChangesInterceptor
{
    private const int RowsPerBatch = 150;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<DbContext, List<Pending>> _pending = [];

    // اگر ذخیره بیرون از تراکنش واحد کار باشد، برای همین ذخیره تراکنش باز می‌شود تا داده و لاگش با هم ثبت یا با هم برگردانده شوند
    private readonly Dictionary<DbContext, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> _owned = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Capture(eventData.Context) && eventData.Context!.Database.CurrentTransaction is null)
            _owned[eventData.Context] = eventData.Context.Database.BeginTransaction();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Capture(eventData.Context) && eventData.Context!.Database.CurrentTransaction is null)
            _owned[eventData.Context] = await eventData.Context.Database.BeginTransactionAsync(cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not { } context) return result;
        try
        {
            FlushAsync(context, CancellationToken.None, sync: true).GetAwaiter().GetResult();
            if (_owned.Remove(context, out var tx)) { tx.Commit(); tx.Dispose(); }
        }
        catch
        {
            Abandon(context);
            throw;
        }
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context) return result;
        try
        {
            await FlushAsync(context, cancellationToken, sync: false);
            if (_owned.Remove(context, out var tx))
            {
                await tx.CommitAsync(cancellationToken);
                await tx.DisposeAsync();
            }
        }
        catch
        {
            Abandon(context);
            throw;
        }
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) Abandon(context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) Abandon(context);
        return Task.CompletedTask;
    }

    /// <summary>ذخیره یا نوشتن لاگ ناموفق بود: تراکنشی که خودمان باز کرده‌ایم برگردانده می‌شود.</summary>
    private void Abandon(DbContext context)
    {
        _pending.Remove(context);
        if (_owned.Remove(context, out var tx)) tx.Dispose();
    }

    // ------------------------------------------------------------------

    private sealed class Pending
    {
        public required EntityEntry Entry { get; init; }
        public required char Action { get; init; }
        public required string Entity { get; init; }
        public long EntityId { get; set; }
        public string? Old { get; init; }
        public string? New { get; set; }
        public (string Entity, long Id)? Parent { get; set; }
    }

    /// <summary>خروجی: تغییری برای لاگ هست یا نه.</summary>
    private bool Capture(DbContext? context)
    {
        if (context is null) return false;
        context.ChangeTracker.DetectChanges();
        var list = new List<Pending>();
        foreach (var e in context.ChangeTracker.Entries())
        {
            switch (e.State)
            {
                case EntityState.Added:
                    list.Add(new Pending { Entry = e, Action = 'I', Entity = NameOf(e) }); // شناسه و مقادیر بعد از ذخیره
                    break;
                case EntityState.Modified:
                    var changed = e.Properties.Where(p => p.IsModified && Auditable(p.Metadata) && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                    if (changed.Count == 0) break;
                    list.Add(new Pending
                    {
                        Entry = e, Action = 'U', Entity = NameOf(e), EntityId = KeyOf(e, original: false),
                        Old = Serialize(KeyValues(e, true).Concat(changed.Select(p => (p.Metadata.Name, p.OriginalValue)))),
                        New = Serialize(KeyValues(e, false).Concat(changed.Select(p => (p.Metadata.Name, p.CurrentValue)))),
                        Parent = ParentOf(context, e, original: false),
                    });
                    break;
                case EntityState.Deleted:
                    list.Add(new Pending
                    {
                        Entry = e, Action = 'D', Entity = NameOf(e), EntityId = KeyOf(e, original: true),
                        Old = Serialize(e.Properties.Where(p => Auditable(p.Metadata) || p.Metadata.IsPrimaryKey())
                            .Select(p => (p.Metadata.Name, p.OriginalValue))),
                        Parent = ParentOf(context, e, original: true),
                    });
                    break;
            }
        }
        if (list.Count > 0) _pending[context] = list;
        else _pending.Remove(context);
        return list.Count > 0;
    }

    private async Task FlushAsync(DbContext context, CancellationToken ct, bool sync)
    {
        if (!_pending.Remove(context, out var list)) return;
        foreach (var p in list.Where(p => p.Action == 'I'))
        {
            p.EntityId = KeyOf(p.Entry, original: false);
            p.New = Serialize(p.Entry.Properties.Where(x => (Auditable(x.Metadata) || x.Metadata.IsPrimaryKey()) && x.CurrentValue is not null)
                .Select(x => (x.Metadata.Name, x.CurrentValue)));
            p.Parent = ParentOf(context, p.Entry, original: false);
        }

        var actorId = user.UserId;
        var userName = user.UserName;
        var ip = services.GetService<IClientInfo>()?.Ip;
        foreach (var batch in list.Chunk(RowsPerBatch))
        {
            var sql = new StringBuilder(
                "INSERT audit.ChangeLog (EntityName, EntityId, [Action], OldValue, NewValue, ActorType, ActorId, OperationId, ParentEntity, ParentId, UserName, Ip) VALUES ");
            var parameters = new List<SqlParameter>();
            for (var i = 0; i < batch.Length; i++)
            {
                var p = batch[i];
                if (i > 0) sql.Append(',');
                sql.Append($"(@e{i},@i{i},@a{i},@o{i},@n{i},@t,@u,@op,@pe{i},@pi{i},@un,@ip)");
                parameters.Add(new SqlParameter($"@e{i}", Cut(p.Entity, 50)));
                parameters.Add(new SqlParameter($"@i{i}", p.EntityId));
                parameters.Add(new SqlParameter($"@a{i}", p.Action.ToString()));
                parameters.Add(new SqlParameter($"@o{i}", (object?)p.Old ?? DBNull.Value));
                parameters.Add(new SqlParameter($"@n{i}", (object?)p.New ?? DBNull.Value));
                parameters.Add(new SqlParameter($"@pe{i}", (object?)(p.Parent is { } pe ? Cut(pe.Entity, 50) : null) ?? DBNull.Value));
                parameters.Add(new SqlParameter($"@pi{i}", (object?)p.Parent?.Id ?? DBNull.Value));
            }
            parameters.Add(new SqlParameter("@t", (byte)(actorId > 0 ? 1 : 3)));
            parameters.Add(new SqlParameter("@u", actorId > 0 ? actorId : DBNull.Value));
            parameters.Add(new SqlParameter("@op", scope.OperationId));
            parameters.Add(new SqlParameter("@un", (object?)Cut(userName, 50) ?? DBNull.Value));
            parameters.Add(new SqlParameter("@ip", (object?)Cut(ip, 45) ?? DBNull.Value));

            if (sync) context.Database.ExecuteSqlRaw(sql.ToString(), parameters);
            else await context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
        }
    }

    private static string NameOf(EntityEntry e) => e.Metadata.ClrType.Name;

    /// <summary>فیلدهای قابل ثبت: نه کلید، نه فایل/باینری، نه نسخه ردیف.</summary>
    private static bool Auditable(IProperty p) =>
        !p.IsPrimaryKey() && !p.IsConcurrencyToken && p.ClrType != typeof(byte[]) && p.ValueGenerated != ValueGenerated.OnAddOrUpdate;

    private static long KeyOf(EntityEntry e, bool original)
    {
        var key = e.Metadata.FindPrimaryKey();
        if (key is not { Properties.Count: 1 }) return 0;
        var prop = e.Property(key.Properties[0].Name);
        return ToLong(original ? prop.OriginalValue : prop.CurrentValue) ?? 0;
    }

    /// <summary>کلید ترکیبی (مثل نقش شخص) در JSON می‌آید چون EntityId تک‌عددی است.</summary>
    private static IEnumerable<(string, object?)> KeyValues(EntityEntry e, bool original)
    {
        var key = e.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 1) return [];
        return key.Properties.Select(p => (p.Name, original ? e.Property(p.Name).OriginalValue : e.Property(p.Name).CurrentValue));
    }

    /// <summary>
    /// ریشه تجمیع: از کلید خارجی‌ای که والدش مجموعه/ارجاع به این موجودیت دارد (مثل Payment.Lines) یا مالکیت (owned)،
    /// تا سه سطح بالا می‌رود (مثلاً چک صادره ← ردیف پرداخت ← پرداخت).
    /// </summary>
    private static (string Entity, long Id)? ParentOf(DbContext context, EntityEntry e, bool original, int depth = 0)
    {
        if (depth > 3) return null;
        foreach (var fk in e.Metadata.GetForeignKeys())
        {
            if (fk.PrincipalToDependent is null && !fk.IsOwnership) continue;
            if (fk.Properties.Count != 1) continue;
            var prop = e.Property(fk.Properties[0].Name);
            if (ToLong(original ? prop.OriginalValue : prop.CurrentValue) is not long id) continue;

            var principal = context.ChangeTracker.Entries()
                .FirstOrDefault(x => x.Metadata == fk.PrincipalEntityType && KeyOf(x, original: x.State == EntityState.Deleted) == id);
            if (principal is not null && ParentOf(context, principal, principal.State == EntityState.Deleted, depth + 1) is { } higher)
                return higher;
            return (fk.PrincipalEntityType.ClrType.Name, id);
        }
        return null;
    }

    private static long? ToLong(object? value) => value switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        _ => null,
    };

    private static string? Serialize(IEnumerable<(string Name, object? Value)> values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (name, value) in values) dict[name] = value;
        return dict.Count == 0 ? null : JsonSerializer.Serialize(dict, Json);
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

public static class CompanyDatabaseExtensions
{
    /// <summary>
    /// اتصال مشترک شرکت، واحد کار و لاگ تغییرات (برای همه ماژول‌ها؛ چند بار صدا زدن مشکلی ندارد).
    /// </summary>
    public static IServiceCollection AddCompanyDatabase(this IServiceCollection services)
    {
        services.TryAddScoped<CompanyConnection>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CompanyConnection>());
        services.TryAddScoped<AuditScope>();
        services.TryAddScoped<AuditInterceptor>();
        return services;
    }

    /// <summary>DbContext ماژول روی اتصال مشترک شرکت، با لاگ تغییرات.</summary>
    public static DbContextOptionsBuilder UseCompanyDatabase(this DbContextOptionsBuilder options, IServiceProvider sp) =>
        options.UseSqlServer(sp.GetRequiredService<CompanyConnection>().Connection)
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
}
