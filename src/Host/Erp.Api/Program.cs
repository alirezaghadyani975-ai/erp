using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Erp.Accounting.Infrastructure;
using Erp.BaseData.Infrastructure;
using Erp.Costing.Infrastructure;
using Erp.Expenses.Infrastructure;
using Erp.Inventory.Infrastructure.Persistence;
using Erp.Payroll.Infrastructure;
using Erp.Personnel.Infrastructure;
using Erp.Production.Infrastructure;
using Erp.Purchases.Infrastructure;
using Erp.Sales.Infrastructure;
using Erp.Treasury.Infrastructure;
using Erp.Security.Application;
using Erp.Security.Infrastructure;
using Erp.Api.Components;
using Erp.Api.Components.Shared;
using Erp.Api.Infrastructure;
using Erp.Database;
using Erp.SharedKernel.Abstractions;
using MudBlazor.Services;
using Scalar.AspNetCore;

// فرهنگ ثابت: قالب عدد و تاریخ نباید به تنظیمات منطقه‌ای ویندوز سرور وابسته باشد (تاریخ شمسی با PersianDate ساخته می‌شود)
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DatabaseMigrator>();

// لاگ خطاهای فنی در فایل روزانه (پوشه logs)
builder.Logging.AddProvider(new FileLoggerProvider(
    Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Logging:File:Path"] ?? "logs")));

// دیتابیس مرکزی (کاربران، نقش‌ها، شرکت‌ها) و احراز هویت
var centralConnection = builder.Configuration.GetConnectionString("Central")
    ?? throw new InvalidOperationException("رشته اتصال دیتابیس مرکزی (ConnectionStrings:Central) تعریف نشده است.");
builder.Services.AddSecurityModule(centralConnection);
builder.Services.AddErpAuth();

// ماژول‌ها — ماژول‌های بعدی (انبار، فروش، خرید، کارگزینی، ...) همین‌جا اضافه می‌شوند
builder.Services.AddBaseDataModule();
builder.Services.AddCostingModule();
builder.Services.AddAccountingModule();
builder.Services.AddExpensesModule();
builder.Services.AddInventoryModule();
builder.Services.AddPurchasesModule();
builder.Services.AddSalesModule();
builder.Services.AddTreasuryModule();
builder.Services.AddPersonnelModule();
builder.Services.AddPayrollModule();
builder.Services.AddProductionModule();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All); // فارسی بدون \u
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi();

// رابط کاربری Blazor (Interactive Server)
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<Backend>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<UiLookups>();
builder.Services.AddScoped<Erp.Api.Components.Shared.Tabs.TabService>();
builder.Services.AddScoped<Erp.Api.Components.Shared.Forms.UiPrefs>();
builder.Services.AddScoped<Erp.Api.Components.Shared.Forms.Shortcuts>();
builder.Services.AddScoped<Erp.Api.Components.Shared.Inputs.VatRateCache>();

var app = builder.Build();

app.UseExceptionHandler();

// دیتابیس مرکزی: اسکریپت‌ها، شرکت‌های appsettings (بخش Companies) و کاربر admin در اولین اجرا
IReadOnlyList<Erp.Security.Domain.Company> companies;
await using (var scope = app.Services.CreateAsyncScope())
{
    var configured = app.Configuration.GetSection("Companies").GetChildren()
        .Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToDictionary(c => c.Key, c => c.Value!);
    companies = await scope.ServiceProvider.GetRequiredService<SecurityBootstrapper>()
        .InitializeAsync(configured, app.Configuration["Security:InitialAdminPassword"] ?? "admin");
    var settings = await scope.ServiceProvider.GetRequiredService<SecuritySettingsService>().GetAsync(default);
    Auth.ApplyIdleTimeout(app.Services, settings.IdleMinutes);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().RequireAuthorization();
    app.MapScalarApiReference(o => o.WithTitle("Erp API")).RequireAuthorization();

    // در محیط توسعه، دیتابیس همه شرکت‌های فعال خودکار ساخته/به‌روز می‌شود
    foreach (var company in companies)
    {
        var result = await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(company.ConnectionString);
        app.Logger.LogInformation("دیتابیس شرکت {Company}: {Created} اسکریپت‌های اجراشده: {Scripts}", company.Key,
            result.DatabaseCreated ? "ساخته شد" : "موجود بود",
            result.AppliedScripts.Count == 0 ? "هیچ (به‌روز است)" : string.Join("، ", result.AppliedScripts));
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseErpSession();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapErpAuthEndpoints();
app.MapControllers().RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();

app.Run();
