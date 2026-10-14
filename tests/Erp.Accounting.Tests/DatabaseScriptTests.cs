using System.Reflection;

namespace Erp.Accounting.Tests;

/// <summary>اسکریپت‌های دیتابیس داخل برنامه جاسازی شده‌اند و به ترتیب درست اجرا می‌شوند.</summary>
public class DatabaseScriptTests
{
    [Fact]
    public void Migration_scripts_are_embedded_in_order()
    {
        var assembly = typeof(Erp.Database.DatabaseMigrator).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql")).Order().ToList();
        Assert.Equal(
            ["Erp.Database.Migrations.01_schema.sql", "Erp.Database.Migrations.02_seed_coding.sql", "Erp.Database.Migrations.03_seed_calendar.sql",
             "Erp.Database.Migrations.05_persons_roles.sql", "Erp.Database.Migrations.06_base_data.sql",
             "Erp.Database.Migrations.07_costing.sql", "Erp.Database.Migrations.08_new_coding.sql"],
            names);
    }
}
