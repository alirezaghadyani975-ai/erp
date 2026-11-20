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
        int Center, int RentFactor, int BankFeeFactor, int ChequeBook, int NoteBook);

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

            var chequeBook = await db.Run<InstrumentService, int>(s => s.CreateBookAsync(new CreateInstrumentBookRequest(
                InstrumentKind.ChequeBook, bank, "دسته‌چک تست", null, 900100, 900149, 0, "1406/01/05", null, null), default));
            var noteBook = await db.Run<InstrumentService, int>(s => s.CreateBookAsync(new CreateInstrumentBookRequest(
                InstrumentKind.PromissoryNoteBook, null, "سفته ۵۰ میلیونی", "الف", 1, 20, 4, "1406/01/05", 50_000_000, null), default));

            return _seed = new Seed(customer, supplier, box, bank, imprest,
                types.Single(t => t.Title == "دریافت از مشتری").Id, types.Single(t => t.Title == "پرداخت به تأمین‌کننده").Id,
                center, factors.Single(f => f.Title == "اجاره مکان").Id, factors.Single(f => f.Title == "کارمزدهای بانکی").Id,
                chequeBook, noteBook);
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

    private async Task<long> LeafAsync(int bookId, string serial) =>
        (await db.Run<InstrumentService, List<InstrumentLeafDto>>(x => x.GetLeavesAsync(bookId, default))).Single(l => l.SerialNo == serial).Id;

    private async Task<LeafStatus> LeafStatusAsync(int bookId, string serial) =>
        (await db.Run<InstrumentService, List<InstrumentLeafDto>>(x => x.GetLeavesAsync(bookId, default))).Single(l => l.SerialNo == serial).Status;

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

        var leaf = await LeafAsync(s.ChequeBook, "900100");
        // چک صادره بدون برگ دسته‌چک پذیرفته نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest(
            "1406/02/10", s.Supplier, s.PaymentType, null, [new TreasuryLineRequest(Instrument.Cheque, null, s.Bank, 5, Cheque: Cheque("1", "1406/05/10"))]), default)));

        var pid = await db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest("1406/02/10", s.Supplier, s.PaymentType, null,
        [
            new TreasuryLineRequest(Instrument.Bank, null, s.Bank, 100),
            new TreasuryLineRequest(Instrument.Cheque, null, null, 900, Cheque: Cheque("", "1406/05/10") with { LeafId = leaf }),
            new TreasuryLineRequest(Instrument.SpendCheque, null, null, 0, ReceivedChequeId: received)
        ]), default));
        var p = await db.Run<PaymentService, ReceiptPaymentDto>(x => x.GetAsync(pid, default));
        Assert.Equal(1400m, p.Total);
        var v = await Voucher(p.Voucher!.VoucherId);
        Assert.Equal(["5011+", "2811-", "5001-", "2401-"], Codes(v));
        Assert.Equal("150005", v.Lines[2].Details.Single()!.Code); // ماه سررسید مرداد
        Assert.Equal("خرج‌شده", (await Received(received)).StatusTitle);

        Assert.Equal("900100", p.Lines[1].Cheque!.SerialNo);
        Assert.Equal(LeafStatus.Used, await LeafStatusAsync(s.ChequeBook, "900100"));

        // برگ مصرف‌شده دوباره استفاده نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest(
            "1406/02/11", s.Supplier, s.PaymentType, null,
            [new TreasuryLineRequest(Instrument.Cheque, null, s.Bank, 5, Cheque: Cheque("", "1406/05/10") with { LeafId = leaf })]), default)));

        var issued = p.Lines[1].Cheque!.Id;
        var clear = await Op(ChequeOperationType.Clear, "1406/05/10", null, null, issued);
        var cv = await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(clear, default));
        Assert.Equal(["5001+", "2811-"], Codes(await Voucher(cv.Voucher!.VoucherId)));
        Assert.NotNull((await db.Run<PaymentService, ReceiptPaymentDto>(x => x.GetAsync(pid, default))).LockReason);

        await db.Run<ChequeOperationService>(x => x.DeleteAsync(clear, default));
        await db.Run<PaymentService>(x => x.DeleteAsync(pid, default));
        Assert.Equal("در صندوق", (await Received(received)).StatusTitle);
        Assert.Equal(LeafStatus.Blank, await LeafStatusAsync(s.ChequeBook, "900100")); // با حذف فرم برگ آزاد شد
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

        // صورت‌هزینه: یک هزینه مشمول ارزش افزوده و یک پرداخت بدهی به تأمین‌کننده از تنخواه
        var sid = await db.Run<ImprestSettlementService, long>(x => x.CreateAsync(new SaveImprestSettlementRequest("1406/02/20", s.Imprest, null,
        [
            new ImprestLineRequest(s.RentFactor, s.Center, 1200, "اجاره انباری", VatAmount: 108),
            new ImprestLineRequest(null, null, 300, null, Kind: ImprestLineKind.Payment, TypeId: s.PaymentType, PersonId: s.Supplier)
        ]), default));
        var st = await db.Run<ImprestSettlementService, ImprestSettlementDto>(x => x.GetAsync(sid, default));
        Assert.Equal((1608m, 108m), (st.Total, st.VatAmount));
        Assert.Equal(["8480+", "5011+", "2541+", "2821-"], Codes(await Voucher(st.Voucher!.VoucherId)));

        // ردیف پرداخت به طرف حساب ارزش افزوده ندارد و طرف حساب می‌خواهد
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ImprestSettlementService, long>(x => x.CreateAsync(new SaveImprestSettlementRequest(
            "1406/02/20", s.Imprest, null, [new ImprestLineRequest(null, null, 10, Kind: ImprestLineKind.Payment, TypeId: s.PaymentType)]), default)));

        var balances = await db.Run<TreasuryReportService, List<FundBalanceDto>>(x => x.BalancesAsync(null, default));
        Assert.Equal(3392m, balances.Single(b => b.Kind == FundKind.Imprest && b.Id == s.Imprest).Balance);
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

        // ضمانت‌نامه بانکی پرداختی فقط از ضمانت‌نامه تعریف‌شده
        await Assert.ThrowsAsync<DomainException>(() => db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(
            GuaranteeDirection.Issued, GuaranteeKind.BankGuarantee, "1406/02/01", s.Customer, s.Bank, "ض-1", 50000, null, null, null), default)));
        var bg = await db.Run<InstrumentService, int>(x => x.SaveBankGuaranteeAsync(null,
            new SaveBankGuaranteeRequest(s.Bank, "ض-1", 50000, "1406/01/20", "1407/01/01", "مناقصه", null), default));
        var issued = await db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(GuaranteeDirection.Issued, GuaranteeKind.BankGuarantee,
            "1406/02/01", s.Customer, null, null, 0, null, "مناقصه", null, BankGuaranteeId: bg), default));
        var ig = await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(issued, default));
        Assert.Equal(("ض-1", 50000m, "1407/01/01"), (ig.Number, ig.Amount, ig.DueDate));
        Assert.Equal(["9152+", "9251-"], Codes(await Voucher(ig.Voucher!.VoucherId)));
        async Task<BankGuaranteeStatus> BgStatus() =>
            (await db.Run<InstrumentService, List<BankGuaranteeDto>>(x => x.GetBankGuaranteesAsync(null, default))).Single(x => x.Id == bg).Status;
        Assert.Equal(BankGuaranteeStatus.Delivered, await BgStatus());
        await Assert.ThrowsAsync<DomainException>(() => db.Run<InstrumentService>(x => x.CancelBankGuaranteeAsync(bg, "1406/03/01", default)));
        await db.Run<GuaranteeService>(x => x.ReturnAsync(issued, "1406/03/01", default));
        Assert.Equal(BankGuaranteeStatus.Available, await BgStatus());
        await db.Run<InstrumentService>(x => x.CancelBankGuaranteeAsync(bg, "1406/03/02", default));
        Assert.Equal(BankGuaranteeStatus.Cancelled, await BgStatus());

        // سفته پرداختی از دفتر سفته، با سقف مبلغ
        var note = await LeafAsync(s.NoteBook, "الف/0001");
        await Assert.ThrowsAsync<DomainException>(() => db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(
            GuaranteeDirection.Issued, GuaranteeKind.PromissoryNote, "1406/02/01", s.Customer, null, null, 60_000_000, null, null, null, LeafId: note), default)));
        var ng = await db.Run<GuaranteeService, long>(x => x.CreateAsync(new SaveGuaranteeRequest(
            GuaranteeDirection.Issued, GuaranteeKind.PromissoryNote, "1406/02/01", s.Customer, null, null, 40_000_000, null, null, null, LeafId: note), default));
        Assert.Equal("الف/0001", (await db.Run<GuaranteeService, GuaranteeDto>(x => x.GetAsync(ng, default))).Number);
        Assert.Equal(LeafStatus.Used, await LeafStatusAsync(s.NoteBook, "الف/0001"));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<InstrumentService>(x => x.DeleteBookAsync(s.NoteBook, default)));
        await db.Run<GuaranteeService>(x => x.DeleteAsync(ng, default));
        Assert.Equal(LeafStatus.Blank, await LeafStatusAsync(s.NoteBook, "الف/0001"));
    }

    [Fact]
    public async Task Instrument_books_leaves_and_overlap()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var books = await db.Run<InstrumentService, List<InstrumentBookDto>>(x => x.GetBooksAsync(InstrumentKind.ChequeBook, default));
        var book = books.Single(b => b.Id == s.ChequeBook);
        Assert.Equal(50, book.Total);
        // هم‌پوشانی بازه با دسته‌چک دیگر همان حساب
        await Assert.ThrowsAsync<DomainException>(() => db.Run<InstrumentService, int>(x => x.CreateBookAsync(new CreateInstrumentBookRequest(
            InstrumentKind.ChequeBook, s.Bank, "تکراری", null, 900140, 900160, 0, "1406/01/05", null, null), default)));

        var leaf = await LeafAsync(s.ChequeBook, "900149");
        await db.Run<InstrumentService>(x => x.VoidLeafAsync(leaf, "1406/01/06", "مخدوش", default));
        var blank = await db.Run<InstrumentService, List<LeafOption>>(x => x.GetBlankLeavesAsync(InstrumentKind.ChequeBook, s.Bank, default));
        Assert.DoesNotContain(blank, l => l.Id == leaf);
        await db.Run<InstrumentService>(x => x.UndoVoidLeafAsync(leaf, default));
        Assert.Equal(LeafStatus.Blank, await LeafStatusAsync(s.ChequeBook, "900149"));
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

    // ------------------------------------------------------------------ اول دوره

    [Fact]
    public async Task Opening_receipt_registers_cheques_without_voucher_and_they_can_be_collected()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var rid = await db.Run<ReceiptService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest("1406/01/01", s.Customer, s.ReceiptType,
            "چک‌های اول دوره", [new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 700, Cheque: Cheque("OP-1", "1406/03/01"))], IsOpening: true), default));
        var r = await Receipt(rid);
        Assert.True(r.IsOpening);
        Assert.Null(r.Voucher);
        var cheque = r.Lines[0].Cheque!.Id;
        Assert.Equal("در صندوق", (await Received(cheque)).StatusTitle);
        var list = await db.Run<ReceiptService, List<ReceiptPaymentListItemDto>>(x => x.ListAsync(null, null, null, default));
        Assert.True(list.Single(x => x.Id == rid).IsOpening);

        // عملیات بعدی روی چک اول دوره سند عادی می‌خورد
        var deposit = await Op(ChequeOperationType.Deposit, "1406/02/01", null, s.Bank, cheque);
        var dv = await db.Run<ChequeOperationService, ChequeOperationDto>(x => x.GetAsync(deposit, default));
        Assert.Equal(["2402+", "2401-"], Codes(await Voucher(dv.Voucher!.VoucherId)));

        // نقد و بانک در فرم اول دوره پذیرفته نمی‌شود
        var ex = await Assert.ThrowsAsync<DomainException>(() => db.Run<ReceiptService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest(
            "1406/01/01", s.Customer, s.ReceiptType, null, [new TreasuryLineRequest(Instrument.Cash, s.Box, null, 5)], IsOpening: true), default)));
        Assert.Contains("اول دوره", ex.Message);
    }

    [Fact]
    public async Task Receipt_switched_to_opening_drops_its_voucher_and_opening_payment_uses_leaf()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var line = new TreasuryLineRequest(Instrument.Cheque, s.Box, null, 300, Cheque: Cheque("OP-2", "1406/04/01"));
        var rid = await ReceiptAsync(s, line);
        Assert.NotNull((await Receipt(rid)).Voucher);
        await db.Run<ReceiptService>(x => x.UpdateAsync(rid, new SaveReceiptPaymentRequest("1406/01/01", s.Customer, s.ReceiptType, null,
            [line], IsOpening: true), default));
        Assert.Null((await Receipt(rid)).Voucher);

        var leaf = await LeafAsync(s.ChequeBook, "900140");
        var pid = await db.Run<PaymentService, long>(x => x.CreateAsync(new SaveReceiptPaymentRequest("1406/01/01", s.Supplier, s.PaymentType,
            "چک صادره پاس‌نشده اول دوره", [new TreasuryLineRequest(Instrument.Cheque, null, null, 800, Cheque: Cheque("", "1406/05/01") with { LeafId = leaf })],
            IsOpening: true), default));
        var p = await db.Run<PaymentService, ReceiptPaymentDto>(x => x.GetAsync(pid, default));
        Assert.True(p.IsOpening);
        Assert.Null(p.Voucher);
        Assert.Equal(LeafStatus.Used, await LeafStatusAsync(s.ChequeBook, "900140"));
    }
}
