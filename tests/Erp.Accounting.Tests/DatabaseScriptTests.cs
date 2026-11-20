using System.Reflection;

namespace Erp.Accounting.Tests;

/// <summary>اسکریپت‌های دیتابیس داخل برنامه جاسازی شده‌اند و به ترتیب درست اجرا می‌شوند.</summary>
public class DatabaseScriptTests
{
    [Fact]
    public void Migration_scripts_are_embedded_in_order()
    {
        var assembly = typeof(Erp.Database.DatabaseMigrator).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Erp.Database.Migrations.") && n.EndsWith(".sql")).Order().ToList();
        Assert.Equal(
            ["Erp.Database.Migrations.01_schema.sql", "Erp.Database.Migrations.02_seed_coding.sql", "Erp.Database.Migrations.03_seed_calendar.sql",
             "Erp.Database.Migrations.05_persons_roles.sql", "Erp.Database.Migrations.06_base_data.sql",
             "Erp.Database.Migrations.07_costing.sql", "Erp.Database.Migrations.08_new_coding.sql",
             "Erp.Database.Migrations.09_standard_order.sql", "Erp.Database.Migrations.10_posting_expenses.sql",
             "Erp.Database.Migrations.11_treasury.sql", "Erp.Database.Migrations.12_vat_lines_instruments.sql",
             "Erp.Database.Migrations.13_audit_log.sql", "Erp.Database.Migrations.14_opening.sql", "Erp.Database.Migrations.15_renumber.sql"],
            names);
    }

    [Fact]
    public void Central_scripts_are_embedded_separately()
    {
        var assembly = typeof(Erp.Database.DatabaseMigrator).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Erp.Database.Central.")).Order().ToList();
        Assert.Equal(["Erp.Database.Central.01_security.sql"], names);
    }
}
