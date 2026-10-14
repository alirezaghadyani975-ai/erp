using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Erp.Database;

/// <summary>
/// ساخت و به‌روزرسانی دیتابیس یک شرکت از روی اسکریپت‌های پوشه database/migrations.
/// هر اسکریپت فقط یک بار اجرا می‌شود و در جدول dbo.SchemaVersions ثبت می‌شود.
/// اسکریپت‌ها به ترتیب نام اجرا می‌شوند؛ اسکریپت جدید را با شماره بعدی اضافه کنید
/// و اسکریپتی را که قبلاً اجرا شده هیچ‌وقت ویرایش نکنید.
/// </summary>
public sealed partial class DatabaseMigrator(ILogger<DatabaseMigrator> logger)
{
    public const string Collation = "Persian_100_CI_AI";
    private const string ResourcePrefix = "Erp.Database.Migrations.";

    [GeneratedRegex(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    public async Task<MigrationResult> MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        var created = await EnsureDatabaseAsync(connectionString, ct);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await EnsureJournalAsync(connection, ct);

        var applied = await GetAppliedAsync(connection, ct);
        var pending = LoadScripts().Where(s => !applied.ContainsKey(s.Name)).ToList();

        foreach (var (name, checksum) in applied)
        {
            var script = LoadScripts().FirstOrDefault(s => s.Name == name);
            if (script is not null && script.Checksum != checksum)
                logger.LogWarning("اسکریپت {Script} بعد از اجرا تغییر کرده است. اسکریپت اجراشده را ویرایش نکنید؛ اسکریپت جدید بسازید.", name);
        }

        foreach (var script in pending)
        {
            logger.LogInformation("اجرای اسکریپت {Script} ...", script.Name);
            await ApplyAsync(connection, script, ct);
        }

        return new MigrationResult(created, pending.Select(s => s.Name).ToList());
    }

    /// <summary>اگر دیتابیس شرکت وجود نداشته باشد، با Collation فارسی ساخته می‌شود.</summary>
    private async Task<bool> EnsureDatabaseAsync(string connectionString, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("نام دیتابیس (Database) در رشته اتصال مشخص نشده است.");

        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(ct);

        await using (var exists = new SqlCommand("SELECT DB_ID(@name)", master))
        {
            exists.Parameters.AddWithValue("@name", database);
            if (await exists.ExecuteScalarAsync(ct) is not DBNull and not null)
                return false;
        }

        logger.LogInformation("ساخت دیتابیس {Database} با Collation {Collation} ...", database, Collation);
        var quoted = "[" + database.Replace("]", "]]") + "]";
        await using (var create = new SqlCommand($"CREATE DATABASE {quoted} COLLATE {Collation};", master))
            await create.ExecuteNonQueryAsync(ct);
        await using (var rcsi = new SqlCommand($"ALTER DATABASE {quoted} SET READ_COMMITTED_SNAPSHOT ON;", master))
            await rcsi.ExecuteNonQueryAsync(ct);

        SqlConnection.ClearAllPools();
        return true;
    }

    private static async Task EnsureJournalAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
            CREATE TABLE dbo.SchemaVersions (
                ScriptName   NVARCHAR(255) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
                Checksum     CHAR(64)      NOT NULL,
                AppliedAtUtc DATETIME2(0)  NOT NULL CONSTRAINT DF_SchemaVersions_At DEFAULT (SYSUTCDATETIME())
            );
            """;
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, string>> GetAppliedAsync(SqlConnection connection, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand("SELECT ScriptName, Checksum FROM dbo.SchemaVersions", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    /// <summary>کل اسکریپت در یک تراکنش اجرا می‌شود؛ اگر جایی خطا بدهد هیچ چیز از آن ثبت نمی‌شود.</summary>
    private static async Task ApplyAsync(SqlConnection connection, Script script, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var batch in SplitBatches(script.Sql))
            {
                await using var cmd = new SqlCommand(batch, connection, tx) { CommandTimeout = 600 };
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var journal = new SqlCommand(
                "INSERT dbo.SchemaVersions (ScriptName, Checksum) VALUES (@n, @c)", connection, tx))
            {
                journal.Parameters.AddWithValue("@n", script.Name);
                journal.Parameters.AddWithValue("@c", script.Checksum);
                await journal.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (SqlException ex)
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException($"اجرای اسکریپت {script.Name} شکست خورد: {ex.Message}", ex);
        }
    }

    internal static IEnumerable<string> SplitBatches(string sql) =>
        BatchSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b));

    internal static IReadOnlyList<Script> LoadScripts()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n => Read(assembly, n))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static Script Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd().Replace("\r\n", "\n");
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        return new Script(resource[ResourcePrefix.Length..], sql, checksum);
    }

    internal sealed record Script(string Name, string Sql, string Checksum);
}

public sealed record MigrationResult(bool DatabaseCreated, IReadOnlyList<string> AppliedScripts);
