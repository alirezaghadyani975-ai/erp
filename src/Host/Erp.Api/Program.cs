using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Erp.Accounting.Infrastructure;
using Erp.BaseData.Infrastructure;
using Erp.Costing.Infrastructure;
using Erp.Expenses.Infrastructure;
using Erp.Api.Components;
using Erp.Api.Components.Shared;
using Erp.Api.Infrastructure;
using Erp.Database;
using Erp.SharedKernel.Abstractions;
using MudBlazor.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DatabaseMigrator>();

// ماژول‌ها — ماژول‌های بعدی (انبار، فروش، خرید، کارگزینی، ...) همین‌جا اضافه می‌شوند
builder.Services.AddBaseDataModule();
builder.Services.AddCostingModule();
builder.Services.AddAccountingModule();
builder.Services.AddExpensesModule();

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
builder.Services.AddScoped<UiLookups>();
builder.Services.AddScoped<Erp.Api.Components.Shared.Tabs.TabService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("Erp API"));

    // در محیط توسعه، دیتابیس شرکت پیش‌فرض خودکار ساخته/به‌روز می‌شود
    var connectionString = app.Configuration.GetSection("Companies")[CompanyContext.DefaultCompany]
        ?? throw new InvalidOperationException("رشته اتصال شرکت پیش‌فرض (Companies:Default) تعریف نشده است.");
    var result = await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(connectionString);
    app.Logger.LogInformation("دیتابیس: {Created} اسکریپت‌های اجراشده: {Scripts}",
        result.DatabaseCreated ? "ساخته شد" : "موجود بود",
        result.AppliedScripts.Count == 0 ? "هیچ (به‌روز است)" : string.Join("، ", result.AppliedScripts));
}

app.UseAntiforgery();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
