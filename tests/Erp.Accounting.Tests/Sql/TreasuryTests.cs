using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Costing.Application.Factors;
using Erp.Costing.Application.Setup;
using Erp.Costing.Contracts;
using Erp.SharedKernel.Domain;
using Erp.Treasury.Application;
using Erp.Treasury.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>خزانه‌داری: دریافت، پرداخت، چک‌ها، انتقال، تنخواه و اسناد تضمینی با سند خودکار.</summary>
[Collection(SqlCollection.Name)]
public class TreasuryTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(int Customer, int Supplier, int Box, int Bank, int Imprest, short ReceiptType, short PaymentType,
        int Center, int RentFactor, int BankFeeFactor);

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;
            await db.Run<FiscalYearService, short>(s => s.CreateAsync(1406, 1, default));
            var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
            short Role(string code) => roles.Single(r => r.Code == code).Id;
            var customer = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "مشتری", LastName = "خزانه", RoleIds = [Role(RoleCodes.Customer)] }, default));
            var supplier = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "تأمین", LastName = "خزانه", RoleIds = [Role(RoleCodes.Supplier)] }, default));

            var box = await db.Run<TreasurySetupService, int>(s => s.SaveCashBoxAsync(null, new SaveCashBoxRequest("صندوق تست خزانه", true, null), default));
            var bank = await db.Run<TreasurySetupService, int>(s => s.SaveBankAccountAsync(null,
                new SaveTreasuryBankAccountRequest("ملت جاری تست", null, "مرکزی", "123456", null, BankAccountKind.Current, true, null), default));
            var imprest = await db.Run<TreasurySetupService, int>(s => s.SaveImprestFundAsync(null,
                new SaveImprestFundRequest("تنخواه تست", supplier, true, null), default));

            var types = await db.Run<TreasurySetupService, List<TransactionTypeDto>>(s => s.GetTypesAsync(null, false, default));
            var types2 = await db.Run<CostSetupService, List<CostCenterTypeDto>>(s => s.GetCenterTypesAsync(true, default));
            var center = await db.Run<CostCenterService, int>(s => s.CreateAsync(
                new SaveCostCenterRequest { Title = "اداری خزانه", CenterTypeId = types2.First(t => t.Function == CostFunction.Admin && t.IsSystem).Id }, default));
            var factors = await db.Run<CostFactorService, List<CostFactorDto>>(s => s.SearchAsync(null, null, true, default));

            return _seed = new Seed(customer, supplier, box, bank, imprest,
                types.Single(t => t.Title == "دریافت از مشتری").Id, types.Single(t => t.Title == "پرداخت به تأمین‌کننده").Id,
                center, factors.Single(f => f.Title == "اجاره مکان").Id, factors.Single(f => f.Title == "کارمزدهای بانکی").Id);
        }
        finally
        {
            Lock.Release();
        }
    }

    private Task<VoucherDto> Voucher(long id) => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default));

    private static string[] Codes(VoucherDto v) => v.Lines.Select(l => $"{l.AccountCode}{(l.Debit > 0 ? "+" : "-")}").ToArray();

    private async Task<long> ReceiptAsync(Seed s, params TreasuryLineRequest[] lines) =>
        await db.Run<ReceiptService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest("1406/02/01", s.Customer, s.ReceiptType, "تست", [.. lines]), default));

    private Task<ReceiptPaymentDto> Receipt(long id) => db.Run<ReceiptService, ReceiptPaymentDto>(x => x.GetAsync(id, default));

    private Task<long> Op(ChequeOperationType op, string date, int? box, int? bank, params long[] cheques) =>
        db.Run<ChequeOperationService, long>(x => x.CreateAsync(new SaveChequeOperationRequest(op, date, box, bank, null, [.. cheques]), default));

    private async Task<ChequeRowDto> Received(long id) =>
        (await db.Run<TreasuryReportService, List<ChequeRowDto>>(x => x.ReceivedChequesAsync(new ChequeFilter(Ids: [id]), default))).Single();

    private static ChequeInput Cheque(string serial, string due, string? sayad = null) => new(serial, sayad, due);

    [Fact]
    public async Task Receipt_posts_cash_bank_and_cheque_against_customer()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var id = await ReceiptAsync(s,
            new TreasuryLineRequest(Instrument.Cash, s.Box, null, 1000),
            new TreasuryLineRequest(Instrument.Bank, null, s.Bank, 2000, "پیگیری 55"),
            new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 3000, Cheque: Cheque("A-1", "1406/03/15", "1234567890123456")));
        var r = await Receipt(id);
        Assert.Equal(6000m, r.Total);
        Assert.Null(r.LockReason);
        Assert.Equal("در صندوق", r.Lines[2].Cheque!.StatusTitle);

        var v = await Voucher(r.Voucher!.VoucherId);
        Assert.Equal(["2801+", "2811+", "2401+", "2411-"], Codes(v));
        Assert.Equal(VoucherStatus.Temporary, v.Status);
        Assert.Equal("A-1", v.Lines[2].FollowUpNo);
        Assert.Equal(6000m, v.Lines[3].Credit);

        // شناسه صیادی تکراری
        var dup = await Assert.ThrowsAsync<DomainException>(() => ReceiptAsync(s,
            new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 10, Cheque: Cheque("A-2", "1406/03/15", "1234567890123456"))));
        Assert.Contains("صیادی", dup.Message);

        var balances = await db.Run<TreasuryReportService, List<FundBalanceDto>>(x => x.BalancesAsync(null, default));
        var box = balances.Single(b => b.Kind == FundKind.CashBox && b.Id == s.Box);
        Assert.True(box.Balance >= 1000 && box.ChequesInBox >= 3000);
    }

    [Fact]
    public async Task Cheque_deposit_collect_and_undo_in_order()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var rid = await ReceiptAsync(s, new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 500, Cheque: Cheque("B-1", "1406/04/01")));
        var cheque = (await Receipt(rid)).Lines[0].Cheque!.Id;

        await Assert.ThrowsAsync<DomainException>(() => Op(ChequeOperationType.Deposit, "1406/02/05", null, null, cheque)); // بانک لازم
        var deposit = await Op(ChequeOperationType.Deposit, "1406/02/05", null, s.Bank, cheque);
        Assert.Equal("در جریان وصول", (await Received(cheque)).StatusTitle);
        var dv = await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(deposit, default));
        Assert.Equal(["2402+", "2401-"], Codes(await Voucher(dv.Voucher!.VoucherId)));

        // فرم دریافت قفل شد
        Assert.NotNull((await Receipt(rid)).LockReason);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ReceiptService>(x => x.DeleteAsync(rid, default)));

        var collect = await Op(ChequeOperationType.Collect, "1406/04/02", null, null, cheque);
        Assert.Equal("وصول‌شده", (await Received(cheque)).StatusTitle);
        var cv = await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(collect, default));
        Assert.Equal(["2811+", "2402-"], Codes(await Voucher(cv.Voucher!.VoucherId)));

        // ترتیب برگشت: اول آخرین عملیات
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ChequeOperationService>(x => x.DeleteAsync(deposit, default)));
        await db.Run<ChequeOperationService>(x => x.DeleteAsync(collect, default));
        await db.Run<ChequeOperationService>(x => x.DeleteAsync(deposit, default));
        Assert.Equal("در صندوق", (await Received(cheque)).StatusTitle);
        await db.Run<ReceiptService>(x => x.DeleteAsync(rid, default));

        var history = await db.Run<TreasuryReportService, List<ChequeRowDto>>(x => x.ReceivedChequesAsync(new ChequeFilter(Term: "B-1"), default));
        Assert.Empty(history);
    }

    [Fact]
    public async Task Bounce_legal_and_return_to_drawer()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var rid = await ReceiptAsync(s, new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 700, Cheque: Cheque("C-1", "1406/02/20")));
        var cheque = (await Receipt(rid)).Lines[0].Cheque!.Id;

        var bounce = await Op(ChequeOperationType.Bounce, "1406/02/21", null, null, cheque);
        Assert.Equal(["2501+", "2401-"], Codes(await Voucher((await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(bounce, default))).Voucher!.VoucherId)));
        var legal = await Op(ChequeOperationType.Legal, "1406/02/25", null, null, cheque);
        Assert.Equal(["2502+", "2501-"], Codes(await Voucher((await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(legal, default))).Voucher!.VoucherId)));
        var back = await Op(ChequeOperationType.ReturnToDrawer, "1406/03/01", null, null, cheque);
        Assert.Equal(["2411+", "2502-"], Codes(await Voucher((await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(back, default))).Voucher!.VoucherId)));
        Assert.Equal("عودت‌شده", (await Received(cheque)).StatusTitle);

        var history = await db.Run<TreasuryReportService, List<ChequeEventDto>>(x => x.HistoryAsync(ChequeSide.Receivable, cheque, default));
        Assert.Equal(4, history.Count);
        Assert.Equal("عودت به صاحب چک", history[^1].Title);

        // عملیات نامجاز
        await Assert.ThrowsAsync<DomainException>(() => Op(ChequeOperationType.Deposit, "1406/03/02", null, s.Bank, cheque));
    }

    [Fact]
    public async Task Payment_with_issued_and_spent_cheques_then_clear()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var rid = await ReceiptAsync(s, new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 400, Cheque: Cheque("D-1", "1406/06/01")));
        var received = (await Receipt(rid)).Lines[0].Cheque!.Id;
        var spendable = await db.Run<PaymentService, List<ChequeDto>>(x => x.GetSpendableChequesAsync(default));
        Assert.Contains(spendable, c => c.Id == received);

        var pid = await db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest("1406/02/10", s.Supplier, s.PaymentType, null,
        [
            new TreasuryLineRequest(Instrument.Bank, null, s.Bank, 100),
            new TreasuryLineRequest(Instrument.Cheque, null, s.Bank, 900, Cheque: Cheque("900100", "1406/05/10")),
            new TreasuryLineRequest(Instrument.SpendCheque, null, null, 0, ReceivedChequeId: received)
        ]), default));
        var p = await db.Run<PaymentService, ReceiptPaymentDto>(x => x.GetAsync(pid, default));
        Assert.Equal(1400m, p.Total);
        var v = await Voucher(p.Voucher!.VoucherId);
        Assert.Equal(["5011+", "2811-", "5001-", "2401-"], Codes(v));
        Assert.Equal("150005", v.Lines[2].Details.Single()!.Code); // ماه سررسید مرداد
        Assert.Equal("خرج‌شده", (await Received(received)).StatusTitle);

        // شماره چک تکراری از همان حساب
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest(
            "1406/02/11", s.Supplier, s.PaymentType, null, [new TreasuryLineRequest(Instrument.Cheque, null, s.Bank, 5, Cheque: Cheque("900100", "1406/05/10"))]), default)));

        var issued = p.Lines[1].Cheque!.Id;
        var clear = await Op(ChequeOperationType.Clear, "1406/05/10", null, null, issued);
        var cv = await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(clear, default));
        Assert.Equal(["5001+", "2811-"], Codes(await Voucher(cv.Voucher!.VoucherId)));
        Assert.NotNull((await db.Run<PaymentService, ReceiptPaymentDto>(x => x.GetAsync(pid, default))).LockReason);

        await db.Run<ChequeOperationService>(x => x.DeleteAsync(clear, default));
        await db.Run<PaymentService>(x => x.DeleteAsync(pid, default));
        Assert.Equal("در صندوق", (await Received(received)).StatusTitle);
    }

    [Fact]
    public async Task Transfer_with_fee_and_imprest_settlement()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var tid = await db.Run<TransferService, long>(x => x.CreateAsync(new SaveTransferRequest("1406/02/12", FundKind.BankAccount, s.Bank,
            FundKind.Imprest, s.Imprest, 5000, 20, s.BankFeeFactor, s.Center, "پایا 1", null), default));
        var t = await db.Run<TransferService, TransferDto>(x => x.GetAsync(tid, default));
        var tv = await Voucher(t.Voucher!.VoucherId);
        Assert.Equal(["2821+", "2811-", "8480+"], Codes(tv));
        Assert.Equal(5020m, tv.Lines[1].Credit);

        await Assert.ThrowsAsync<DomainException>(() => db.Run<TransferService, long>(x => x.CreateAsync(new SaveTransferRequest("1406/02/12",
            FundKind.BankAccount, s.Bank, FundKind.Imprest, s.Imprest, 10, 5, s.BankFeeFactor, null, null, null), default))); // مرکز لازم

        var sid = await db.Run<ImprestSettlementService, long>(x => x.CreateAsync(new SaveImprestSettlementRequest("1406/02/20", s.Imprest, 0, null,
            [new ImprestLineRequest(s.RentFactor, s.Center, 1200, "اجاره انباری")]), default));
        var st = await db.Run<ImprestSettlementService, ImprestSettlementDto>(x => x.GetAsync(sid, default));
        Assert.Equal(["8480+", "2821-"], Codes(await Voucher(st.Voucher!.VoucherId)));

        var balances = await db.Run<TreasuryReportService, List<FundBalanceDto>>(x => x.BalancesAsync(null, default));
        Assert.Equal(3800m, balances.Single(b => b.Kind == FundKind.Imprest && b.Id == s.Imprest).Balance);
        var asOf = await db.Run<TreasuryReportService, List<FundBalanceDto>>(x => x.BalancesAsync("1406/02/15", default));
        Assert.Equal(5000m, asOf.Single(b => b.Kind == FundKind.Imprest && b.Id == s.Imprest).Balance);
    }

    [Fact]
    public async Task Guarantee_register_return_and_undo()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var gid = await db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(GuaranteeDirection.Received, GuaranteeKind.PromissoryNote,
            "1406/02/01", s.Supplier, null, "س-12", 10000, null, "حسن انجام کار", null), default));
        var g = await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(gid, default));
        Assert.Equal(["9101+", "9201-"], Codes(await Voucher(g.Voucher!.VoucherId)));

        await db.Run<GuaranteeService>(x => x.ReturnAsync(gid, "1406/06/01", default));
        g = await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(gid, default));
        Assert.True(g.IsReturned);
        Assert.Equal(["9101-", "9201+"], Codes(await Voucher(g.ReturnVoucher!.VoucherId)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<GuaranteeService>(x => x.DeleteAsync(gid, default)));

        await db.Run<GuaranteeService>(x => x.UndoReturnAsync(gid, default));
        g = await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(gid, default));
        Assert.Null(g.ReturnVoucher);

        var issued = await db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(GuaranteeDirection.Issued, GuaranteeKind.BankGuarantee,
            "1406/02/01", s.Customer, s.Bank, "ض-1", 50000, "1407/01/01", "مناقصه", null), default));
        var ig = await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(issued, default));
        Assert.Equal(["9152+", "9251-"], Codes(await Voucher(ig.Voucher!.VoucherId)));
    }

    [Fact]
    public async Task Treasury_details_are_managed_by_treasury()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var boxes = await db.Run<TreasurySetupService, List<CashBoxDto>>(x => x.GetCashBoxesAsync(true, default));
        Assert.StartsWith("1000", boxes.Single(b => b.Id == s.Box).Code);
        var types = await db.Run<Erp.Accounting.Application.Details.DetailService, List<Erp.Accounting.Application.Details.DetailTypeDto>>(
            x => x.GetTypesAsync(default));
        var cash = types.Single(t => t.CodeFrom == "100001").Id;
        await Assert.ThrowsAsync<DomainException>(() => db.Run<Erp.Accounting.Application.Details.DetailService, int>(x =>
            x.CreateAccountAsync(new Erp.Accounting.Application.Details.CreateDetailAccountRequest(cash, "دستی", null), default)));

        // صندوق استفاده‌شده حذف نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<TreasurySetupService>(x => x.DeleteCashBoxAsync(s.Box, default)));
        var spare = await db.Run<TreasurySetupService, int>(x => x.SaveCashBoxAsync(null, new SaveCashBoxRequest("صندوق موقت", true, null), default));
        await db.Run<TreasurySetupService>(x => x.DeleteCashBoxAsync(spare, default));
    }
}
