using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Posting;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Costing.Application.Factors;
using Erp.Costing.Application.Setup;
using Erp.Costing.Contracts;
using Erp.Expenses.Application;
using Erp.Expenses.Domain;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>شناسایی هزینه و موتور صدور سند خودکار.</summary>
[Collection(SqlCollection.Name)]
public class ExpenseTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(short YearId, int Supplier, int Production, int Admin, int Repair, int Rent, int Interest);

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;
            var year = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1407, 1, default));
            var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
            var supplierRole = roles.Single(r => r.Code == RoleCodes.Supplier).Id;
            var supplier = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "پیمانکار", LastName = "خدمات", RoleIds = [supplierRole] }, default));

            var types = await db.Run<CostSetupService, List<CostCenterTypeDto>>(s => s.GetCenterTypesAsync(true, default));
            async Task<int> Center(string title, CostFunction f) => await db.Run<CostCenterService, int>(s => s.CreateAsync(
                new SaveCostCenterRequest { Title = title, CenterTypeId = types.First(t => t.Function == f && t.IsSystem).Id }, default));
            var production = await Center("خط تولید هزینه", CostFunction.Production);
            var admin = await Center("اداری هزینه", CostFunction.Admin);

            var factors = await db.Run<CostFactorService, List<CostFactorDto>>(s => s.SearchAsync(null, null, true, default));
            int Factor(string title) => factors.Single(f => f.Title == title).Id;
            return _seed = new Seed(year, supplier, production, admin, Factor("تعمیر و نگهداری ماشین‌آلات و تجهیزات"), Factor("اجاره مکان"),
                Factor("سود تضمین‌شده و کارمزد تسهیلات"));
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>ارزش افزوده روی ردیف اول (بقیه معاف).</summary>
    private static SaveExpenseInvoiceRequest Request(Seed s, string? invoiceNo, decimal vat, PayableKind kind,
        params ExpenseLineRequest[] lines) =>
        new("1407/02/15", s.Supplier, kind, invoiceNo, "1407/02/10", "تست شناسایی هزینه",
            [lines[0] with { VatAmount = vat }, .. lines.Skip(1)]);

    private Task<ExpenseInvoiceDto> Get(long id) => db.Run<ExpenseInvoiceService, ExpenseInvoiceDto>(s => s.GetAsync(id, default));

    private Task<VoucherDto> Voucher(long id) => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default));

    [Fact]
    public async Task Saving_posts_temporary_voucher_with_cost_accounts_and_details()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var id = await db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(Request(s, "A-100", 90, PayableKind.Trade,
            new ExpenseLineRequest(s.Repair, s.Production, 700, "تعمیر پرس"),
            new ExpenseLineRequest(s.Rent, s.Admin, 300),
            new ExpenseLineRequest(s.Interest, s.Production, 50)), default));

        var invoice = await Get(id);
        Assert.Equal((1050m, 1140m), (invoice.LinesTotal, invoice.Total));
        Assert.Equal([90m, 0m, 0m], invoice.Lines.OrderBy(l => l.RowNo).Select(l => l.VatAmount)); // مشمول و معاف در یک فرم
        Assert.Null(invoice.Lines.Single(l => l.CostFactorId == s.Interest).CostCenterId); // هزینه مالی مرکز ندارد
        var posted = Assert.IsType<ExpenseVoucherDto>(invoice.Voucher);
        Assert.Equal((byte)VoucherStatus.Temporary, posted.Status);

        var v = await Voucher(posted.VoucherId);
        Assert.Equal(("expenses", "expense_invoice", id), (v.SourceModule, v.SourceDocType, v.SourceDocumentId!.Value));
        Assert.True(v.IsBalanced);
        Assert.Equal(["8410", "8480", "8990", "2541", "5011"], v.Lines.Select(l => l.AccountCode));
        Assert.Equal(2, v.Lines[0].Details.Count);               // عامل و مرکز
        Assert.Single(v.Lines[2].Details);                         // هزینه مالی فقط عامل
        Assert.Empty(v.Lines[3].Details);                          // ارزش افزوده بدون تفصیلی
        Assert.Equal(1140m, v.Lines[4].Credit);
        Assert.StartsWith("6", v.Lines[4].Details.Single()!.Code); // تفصیلی شخص حقیقی
        Assert.Equal("تعمیر پرس", v.Lines[0].Description);
    }

    [Fact]
    public async Task Editing_rewrites_same_voucher_and_delete_removes_it()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var id = await db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(Request(s, "B-1", 0, PayableKind.Trade,
            new ExpenseLineRequest(s.Rent, s.Admin, 500)), default));
        var before = (await Get(id)).Voucher!;

        await db.Run<ExpenseInvoiceService>(x => x.UpdateAsync(id, Request(s, "B-1", 10, PayableKind.Other,
            new ExpenseLineRequest(s.Rent, s.Admin, 400), new ExpenseLineRequest(s.Repair, s.Production, 100)), default));
        var after = (await Get(id)).Voucher!;
        Assert.Equal((before.VoucherId, before.TempNo), (after.VoucherId, after.TempNo));
        var v = await Voucher(after.VoucherId);
        Assert.Equal(["8480", "8410", "2541", "5111"], v.Lines.Select(l => l.AccountCode));
        Assert.Equal(510m, v.TotalDebit);

        // سند خودکار از فرم سند اصلاح یا حذف نمی‌شود
        var manual = await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService>(x => x.ReturnToDraftAsync(after.VoucherId, default)));
        Assert.Contains("شناسایی هزینه", manual.Message);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService>(x => x.VoidAsync(after.VoucherId, default)));

        await db.Run<ExpenseInvoiceService>(x => x.DeleteAsync(id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Voucher(after.VoucherId));
    }

    [Fact]
    public async Task Permanent_voucher_locks_the_form()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var id = await db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(Request(s, "C-1", 0, PayableKind.Trade,
            new ExpenseLineRequest(s.Rent, s.Admin, 250)), default));
        var voucher = (await Get(id)).Voucher!;
        await db.Run<VoucherService, int>(x => x.MarkPermanentAsync(voucher.VoucherId, default));

        var edit = await Assert.ThrowsAsync<DomainException>(() => db.Run<ExpenseInvoiceService>(x => x.UpdateAsync(id,
            Request(s, "C-1", 0, PayableKind.Trade, new ExpenseLineRequest(s.Rent, s.Admin, 260)), default)));
        Assert.Contains("دائم", edit.Message);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ExpenseInvoiceService>(x => x.DeleteAsync(id, default)));
        Assert.Equal(250m, (await Get(id)).Total);
    }

    [Fact]
    public async Task Validation_errors_keep_nothing()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var count = (await db.Run<ExpenseInvoiceService, List<ExpenseInvoiceListItemDto>>(x => x.ListAsync(null, null, null, default))).Count;

        var noCenter = await Assert.ThrowsAsync<DomainException>(() => db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(
            Request(s, null, 0, PayableKind.Trade, new ExpenseLineRequest(s.Rent, null, 100)), default)));
        Assert.Contains("مرکز هزینه", noCenter.Message);

        // تاریخ خارج از سال مالی: سند صادر نمی‌شود و فرم هم ثبت نمی‌شود
        var noYear = await Assert.ThrowsAsync<DomainException>(() => db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(
            Request(s, null, 0, PayableKind.Trade, new ExpenseLineRequest(s.Rent, s.Admin, 100)) with { Date = "1399/01/10", InvoiceDate = null }, default)));
        Assert.Contains("سال مالی", noYear.Message);

        await db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(Request(s, "D-1", 0, PayableKind.Trade,
            new ExpenseLineRequest(s.Rent, s.Admin, 100)), default));
        var duplicate = await Assert.ThrowsAsync<DomainException>(() => db.Run<ExpenseInvoiceService, long>(x => x.CreateAsync(
            Request(s, "D-1", 0, PayableKind.Trade, new ExpenseLineRequest(s.Rent, s.Admin, 100)), default)));
        Assert.Contains("D-1", duplicate.Message);

        Assert.Equal(count + 1, (await db.Run<ExpenseInvoiceService, List<ExpenseInvoiceListItemDto>>(x => x.ListAsync(null, null, null, default))).Count);
    }

    [Fact]
    public async Task Template_account_can_be_changed_and_is_validated()
    {
        if (!db.Enabled) return;
        var templates = await db.Run<PostingTemplateService, List<PostingTemplateDto>>(x => x.GetAllAsync(default));
        var t = templates.Single(x => x.SourceDocType == "expense_invoice");
        Assert.Equal(["expense", "vat", "payable_trade", "payable_other"], t.Lines.Select(l => l.LineKey));
        Assert.Equal("cost", t.Lines[0].AccountRule);
        var vat = t.Lines[1];
        Assert.Equal("2541", vat.AccountCode);

        await Assert.ThrowsAsync<DomainException>(() => db.Run<PostingTemplateService>(x => x.ChangeAccountAsync(t.Id, vat.Id, "25", default)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PostingTemplateService>(x => x.ChangeAccountAsync(t.Id, t.Lines[0].Id, "8410", default)));
        await db.Run<PostingTemplateService>(x => x.ChangeAccountAsync(t.Id, vat.Id, "2511", default));
        var changed = (await db.Run<PostingTemplateService, List<PostingTemplateDto>>(x => x.GetAllAsync(default)))
            .Single(x => x.Id == t.Id).Lines[1];
        Assert.Equal("2511", changed.AccountCode);
        await db.Run<PostingTemplateService>(x => x.ChangeAccountAsync(t.Id, vat.Id, "2541", default));
    }
}
