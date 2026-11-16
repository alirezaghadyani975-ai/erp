using System.Collections.Concurrent;

namespace Erp.Security.Application;

/// <summary>
/// محدودیت تلاش ورود ناموفق به ازای هر نشانی IP (در حافظه): بعد از <see cref="MaxFailures"/> ورود ناموفق در
/// <see cref="Window"/>، تلاش‌های بعدی آن IP تا پایان بازه بدون بررسی رمز رد می‌شوند. جلوی حدس زدن انبوه رمز،
/// قفل کردن پشت‌سرهم حساب‌ها و مصرف پردازنده (هر بررسی رمز سنگین است) را می‌گیرد.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new();

    public bool IsBlocked(string? ip)
    {
        if (string.IsNullOrEmpty(ip) || !_failures.TryGetValue(ip, out var queue)) return false;
        lock (queue)
        {
            Trim(queue);
            return queue.Count >= MaxFailures;
        }
    }

    public void RegisterFailure(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return;
        var queue = _failures.GetOrAdd(ip, _ => new Queue<DateTime>());
        lock (queue)
        {
            Trim(queue);
            queue.Enqueue(clock.GetUtcNow().UtcDateTime);
        }
        if (_failures.Count > 10_000) Sweep();
    }

    private void Trim(Queue<DateTime> queue)
    {
        var limit = clock.GetUtcNow().UtcDateTime - Window;
        while (queue.Count > 0 && queue.Peek() < limit) queue.Dequeue();
    }

    private void Sweep()
    {
        foreach (var (ip, queue) in _failures)
        {
            lock (queue)
            {
                Trim(queue);
                if (queue.Count == 0) _failures.TryRemove(ip, out _);
            }
        }
    }
}
