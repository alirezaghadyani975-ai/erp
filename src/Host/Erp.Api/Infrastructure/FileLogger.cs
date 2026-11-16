using System.Collections.Concurrent;
using System.Text;

namespace Erp.Api.Infrastructure;

/// <summary>
/// لاگ فنی ساده در فایل روزانه (logs/erp-1405-07-30.log، به تاریخ میلادی در نام فایل: erp-2026-10-22.log).
/// فقط هشدار و خطا نوشته می‌شود؛ هر خطای پیش‌بینی‌نشده یک کد پیگیری دارد که به کاربر هم نشان داده می‌شود.
/// فایل‌های قدیمی‌تر از ۹۰ روز پاک می‌شوند.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _gate = new();
    private DateOnly _cleanedOn;

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, c => new FileLogger(this, c));

    public void Dispose() { }

    internal void Write(string text)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_directory, $"erp-{today:yyyy-MM-dd}.log");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, text, Encoding.UTF8);
                if (_cleanedOn != today)
                {
                    _cleanedOn = today;
                    foreach (var old in Directory.GetFiles(_directory, "erp-*.log")
                                 .Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-90)))
                        File.Delete(old);
                }
            }
            catch (IOException) { } // لاگ نباید برنامه را از کار بیندازد
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(logLevel).Append("] ")
              .Append(category).Append(": ").AppendLine(formatter(state, exception));
            if (exception is not null) sb.AppendLine(exception.ToString());
            provider.Write(sb.ToString());
        }
    }
}

/// <summary>کد پیگیری کوتاه برای خطای پیش‌بینی‌نشده (در لاگ و پیام کاربر).</summary>
public static class ErrorCode
{
    public static string New() => "E-" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 4);

    public static string Message(string code) =>
        $"خطای پیش‌بینی‌نشده رخ داد (کد پیگیری {code}). این کد را به پشتیبانی بدهید؛ جزئیات در لاگ برنامه ثبت شده است.";
}
