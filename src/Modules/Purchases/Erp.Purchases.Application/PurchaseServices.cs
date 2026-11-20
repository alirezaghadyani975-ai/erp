using Erp.Accounting.Contracts;
using Erp.Inventory.Contracts;
using Erp.Purchases.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Purchases.Application;

/// <summary>
/// فاکتور خرید داخلی کالا. فرم و سند حسابداری‌اش در یک تراکنش ثبت، اصلاح و حذف می‌شوند.
/// فاکتوری که برگشت دارد اصلاح و حذف نمی‌شود (اول برگشت‌ها حذف شوند).
/// </summary>
public class PurchaseInvoiceService(IPurchasesDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user,
    TimeProvider clock, IAccessControl access)
{
    public async Task<List<PurchaseInvoiceListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceView);
        var query = db.PurchaseInvoices.AsNoTracking();
        if (access.OwnDocsOnly) query = query.Where(i => i.CreatedBy == access.UserId);
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(i => i.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(i => i.DocDate <= t); }

        var rows = await query
            .Select(i => new
            {
                i.Id, i.DocNo, i.DocDate, i.SupplierId, i.InvoiceNo, i.GrossAmount, i.DiscountAmount, i.VatAmount, i.Description,
                Supplier = db.Parties.Where(s => s.Id == i.SupplierId).Select(s => s.DisplayName).FirstOrDefault()
            })
            .OrderByDescending(i => i.DocDate).ThenByDescending(i => i.DocNo)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Supplier ?? "").Contains(term) || (r.InvoiceNo ?? "").Contains(term)
                                   || (r.Description ?? "").Contains(term) || r.DocNo.ToString() == term).ToList();

        var vouchers = await posting.FindAsync(PurchaseInvoice.Module, PurchaseInvoice.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new PurchaseInvoiceListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.SupplierId,
            r.Supplier ?? "?", r.InvoiceNo, r.GrossAmount - r.DiscountAmount, r.DiscountAmount, r.VatAmount,
            r.GrossAmount - r.DiscountAmount + r.VatAmount, r.Description, PurchaseVoucherDto.Of(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<PurchaseInvoiceDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceView);
        var invoice = await LoadAsync(id, tracking: false, ct);
        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var lineIds = invoice.Lines.Select(l => l.Id).ToList();
        var returned = await db.PurchaseReturns.SelectMany(r => r.Lines).Where(l => lineIds.Contains(l.InvoiceLineId))
            .GroupBy(l => l.InvoiceLineId).Select(g => new { g.Key, Qty = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var returnCount = await db.PurchaseReturns.CountAsync(r => r.InvoiceId == id, ct);
        var supplier = await db.Parties.Where(s => s.Id == invoice.SupplierId).Select(s => s.DisplayName).FirstOrDefaultAsync(ct);
        var vouchers = await posting.FindAsync(PurchaseInvoice.Module, PurchaseInvoice.DocType, [id], ct);
        var receiptIds = invoice.Lines.Where(l => l.ReceiptLineId is not null).Select(l => l.ReceiptLineId!.Value).ToList();
        var receipts = await db.StockLines.Where(s => receiptIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);

        return new PurchaseInvoiceDto(invoice.Id, invoice.DocNo, PersianDate.ToPersian(invoice.DocDate), invoice.SupplierId,
            supplier ?? "?", invoice.PayableKind, invoice.InvoiceNo, invoice.InvoiceDate is { } d ? PersianDate.ToPersian(d) : null,
            invoice.SellerTaxId, invoice.Description, invoice.GrossAmount, invoice.DiscountAmount, invoice.NetAmount, invoice.VatAmount,
            invoice.Total,
            invoice.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var item = items.GetValueOrDefault(l.ItemId);
                return new PurchaseLineDto(l.Id, l.RowNo, l.ItemId, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "",
                    l.Quantity, l.UnitPrice, l.Discount, l.Amount, l.VatAmount, l.Description, returned.GetValueOrDefault(l.Id),
                    l.ReceiptLineId, l.ReceiptLineId is long r ? receipts.GetValueOrDefault(r) : null);
            }).ToList(),
            PurchaseVoucherDto.Of(vouchers.GetValueOrDefault(id)), returnCount);
    }

    /// <summary>ردیف‌های رسید خرید تأمین‌کننده که هنوز فاکتور نشده‌اند (ردیف‌های همین فاکتور در حال اصلاح هم می‌آیند).</summary>
    public async Task<List<PendingStockLineDto>> PendingReceiptsAsync(int supplierId, long? exceptInvoiceId, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceView);
        var invoiced = db.PurchaseInvoices.Where(i => i.Id != (exceptInvoiceId ?? 0)).SelectMany(i => i.Lines)
            .Where(l => l.ReceiptLineId != null).Select(l => l.ReceiptLineId!.Value);
        var lines = await db.StockLines
            .Where(s => s.DocType == StockDocType.PurchaseReceipt && s.PartyId == supplierId && !invoiced.Contains(s.Id))
            .OrderBy(s => s.DocDate).ThenBy(s => s.DocNo).ThenBy(s => s.RowNo).ToListAsync(ct);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return lines.Select(s =>
        {
            var item = items.GetValueOrDefault(s.ItemId);
            return new PendingStockLineDto(s.Id, s.DocId, s.DocType, s.DocNo, PersianDate.ToPersian(s.DocDate), s.WarehouseTitle, s.RowNo,
                s.ItemId, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "", s.Quantity, s.EnteredPrice);
        }).ToList();
    }

    /// <summary>ثبت فاکتور و صدور سند موقت آن.</summary>
    public async Task<long> CreateAsync(SavePurchaseInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceCreate);
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.PurchaseInvoices.MaxAsync(i => (int?)i.DocNo, ct) ?? 0) + 1;
        var invoice = new PurchaseInvoice(docNo, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (supplier, items) = await ApplyAsync(invoice, request, ct);
        db.Add(invoice);
        await db.SaveChangesAsync(ct);

        await posting.PostAsync(PostingRequest(invoice, supplier, items), ct);
        await tx.CommitAsync(ct);
        return invoice.Id;
    }

    /// <summary>اصلاح فاکتور؛ سندش با همان شماره بازنویسی می‌شود (اگر دائم نشده باشد).</summary>
    public async Task UpdateAsync(long id, SavePurchaseInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceEdit);
        await posting.EnsureChangeableAsync(PurchaseInvoice.Module, PurchaseInvoice.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);
        await EnsureNoReturnsAsync(invoice, "اصلاح", ct);

        invoice.ClearLines();
        await db.SaveChangesAsync(ct); // ردیف‌های قبلی پیش از درج ردیف‌های تازه حذف شوند
        var (supplier, items) = await ApplyAsync(invoice, request, ct);
        invoice.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        await posting.PostAsync(PostingRequest(invoice, supplier, items), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.InvoiceDelete);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);
        await EnsureNoReturnsAsync(invoice, "حذف", ct);
        await posting.UnpostAsync(PurchaseInvoice.Module, PurchaseInvoice.DocType, id, ct);
        db.Remove(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task EnsureNoReturnsAsync(PurchaseInvoice invoice, string action, CancellationToken ct)
    {
        var returns = await db.PurchaseReturns.Where(r => r.InvoiceId == invoice.Id).Select(r => r.DocNo).ToListAsync(ct);
        if (returns.Count > 0)
            throw new DomainException($"فاکتور خرید {invoice.DocNo} برگشت دارد (برگشت شماره {string.Join("، ", returns)}) و {action} نمی‌شود؛ " +
                                      "اول برگشت‌ها را حذف کنید.");
    }

    private async Task<(PartyRef Supplier, Dictionary<int, ItemRef> Items)> ApplyAsync(PurchaseInvoice invoice,
        SavePurchaseInvoiceRequest request, CancellationToken ct)
    {
        var supplier = await db.Parties.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
                       ?? throw new DomainException("تأمین‌کننده انتخاب نشده یا پیدا نشد.");
        if (!supplier.IsActive && supplier.Id != invoice.SupplierId)
            throw new DomainException($"شخص «{supplier.DisplayName}» غیرفعال است.");

        var previous = invoice.Lines.Select(l => l.ItemId).ToHashSet();
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        // هر ردیف از یک ردیف رسید خرید همین تأمین‌کننده که در فاکتور دیگری نیامده، با همان کالا و مقدار
        var receiptIds = request.Lines.Where(l => l.ReceiptLineId is not null).Select(l => l.ReceiptLineId!.Value).ToList();
        if (receiptIds.Distinct().Count() != receiptIds.Count) throw new DomainException("یک ردیف رسید دو بار در فاکتور آمده است.");
        var receipts = await db.StockLines.Where(s => receiptIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var taken = await db.PurchaseInvoices.Where(i => i.Id != invoice.Id).SelectMany(i => i.Lines.Select(l => new { l.ReceiptLineId, i.DocNo }))
            .Where(x => x.ReceiptLineId != null && receiptIds.Contains(x.ReceiptLineId.Value))
            .ToDictionaryAsync(x => x.ReceiptLineId!.Value, x => x.DocNo, ct);

        var row = 0;
        foreach (var line in request.Lines)
        {
            row++;
            var item = items.GetValueOrDefault(line.ItemId) ?? throw new DomainException($"ردیف {row}: کالا انتخاب نشده یا پیدا نشد.");
            if (!item.IsActive && !previous.Contains(item.Id)) throw new DomainException($"ردیف {row}: «{item.Title}» غیرفعال است.");
            if (item.Kind == ItemKind.Service || item.PurchaseAccountId is null)
                throw new DomainException($"ردیف {row}: «{item.Title}» خدمت است؛ خرید خدمت را با «شناسایی هزینه» ثبت کنید.");
            if (item.VatExempt && line.VatAmount != 0)
                throw new DomainException($"ردیف {row}: «{item.Title}» معاف از ارزش افزوده است.");

            var receipt = line.ReceiptLineId is long rid ? receipts.GetValueOrDefault(rid) : null;
            if (receipt is null || receipt.DocType != StockDocType.PurchaseReceipt)
                throw new DomainException($"ردیف {row}: «{item.Title}» باید از رسید خرید انتخاب شود (اول رسید انبار، بعد فاکتور).");
            if (receipt.PartyId != request.SupplierId)
                throw new DomainException($"ردیف {row}: {receipt.Title} مال تأمین‌کننده این فاکتور نیست.");
            if (taken.TryGetValue(receipt.Id, out var takenBy))
                throw new DomainException($"ردیف {row}: {receipt.Title} قبلاً در فاکتور خرید {takenBy:000000} آمده است.");
            if (receipt.ItemId != line.ItemId || receipt.Quantity != line.Quantity)
                throw new DomainException($"ردیف {row}: کالا و مقدار باید همان {receipt.Title} باشد.");
        }

        var date = PersianDate.Parse(request.Date);
        DateOnly? invoiceDate = string.IsNullOrWhiteSpace(request.InvoiceDate) ? null : PersianDate.Parse(request.InvoiceDate);
        invoice.SetHeader(date, supplier.Id, request.PayableKind, request.InvoiceNo, invoiceDate, request.SellerTaxId, request.Description);
        invoice.SetLines(request.Lines.Select(l => new PurchaseLineInput(l.ItemId, l.Quantity, l.UnitPrice, l.Discount, l.VatAmount, l.Description,
            l.ReceiptLineId!.Value)));

        if (invoice.InvoiceNo is { } no
            && await db.PurchaseInvoices.Where(i => i.Id != invoice.Id && i.SupplierId == supplier.Id && i.InvoiceNo == no)
                .Select(i => (int?)i.DocNo).FirstOrDefaultAsync(ct) is int other)
            throw new DomainException($"فاکتور شماره {no} از «{supplier.DisplayName}» قبلاً در فاکتور خرید {other} ثبت شده است.");
        return (supplier, items);
    }

    private static PostingRequest PostingRequest(PurchaseInvoice invoice, PartyRef supplier, Dictionary<int, ItemRef> items)
    {
        var description = Cut($"فاکتور خرید {invoice.DocNo} - {supplier.DisplayName}"
                              + (invoice.InvoiceNo is { } no ? $" - فاکتور {no}" : "")
                              + (invoice.Description is { } d ? $" - {d}" : ""));

        var lines = invoice.Lines.GroupBy(l => items[l.ItemId].PurchaseAccountId!.Value)
            .Select(g => new PostingLine("inventory", g.Sum(l => l.Amount), [], $"خرید کالا طبق فاکتور خرید {invoice.DocNo}", AccountId: g.Key))
            .ToList();
        if (invoice.VatAmount > 0)
            lines.Add(new PostingLine("vat", invoice.VatAmount, [], $"ارزش افزوده فاکتور خرید {invoice.DocNo}"));
        lines.Add(new PostingLine(invoice.PayableKind == PayableKind.Trade ? "payable_trade" : "payable_other", invoice.Total,
            [new PostingDetail(PostingDetailKind.Person, supplier.Id)], invoice.InvoiceNo is { } inv ? $"فاکتور {inv}" : null));

        return new PostingRequest(PurchaseInvoice.Module, PurchaseInvoice.DocType, invoice.Id, invoice.DocDate, description, lines);
    }

    private async Task<PurchaseInvoice> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.PurchaseInvoices.Include(i => i.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var invoice = await query.FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException($"فاکتور خرید با شناسه {id} پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        return invoice;
    }

    internal static string Cut(string text) => text.Length > 500 ? text[..500] : text;
}

/// <summary>
/// برگشت از خرید با ارجاع به فاکتور خرید. مبلغ و ارزش افزوده هر ردیف به نسبت مقدار از ردیف فاکتور حساب می‌شود
/// (آخرین برگشت یک ردیف باقی مبلغ را می‌گیرد).
/// </summary>
public class PurchaseReturnService(IPurchasesDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user,
    TimeProvider clock, IAccessControl access)
{
    public async Task<List<PurchaseReturnListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnView);
        var query = db.PurchaseReturns.AsNoTracking();
        if (access.OwnDocsOnly) query = query.Where(r => r.CreatedBy == access.UserId);
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }

        var rows = await query
            .Select(r => new
            {
                r.Id, r.DocNo, r.DocDate, r.InvoiceId, r.Amount, r.VatAmount, r.Description,
                Invoice = db.PurchaseInvoices.Where(i => i.Id == r.InvoiceId).Select(i => new
                {
                    i.DocNo,
                    Supplier = db.Parties.Where(p => p.Id == i.SupplierId).Select(p => p.DisplayName).FirstOrDefault()
                }).First()
            })
            .OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Invoice.Supplier ?? "").Contains(term) || (r.Description ?? "").Contains(term)
                                   || r.DocNo.ToString() == term || r.Invoice.DocNo.ToString() == term).ToList();

        var vouchers = await posting.FindAsync(PurchaseInvoice.Module, PurchaseReturn.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new PurchaseReturnListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.InvoiceId, r.Invoice.DocNo,
            r.Invoice.Supplier ?? "?", r.Amount, r.VatAmount, r.Amount + r.VatAmount, r.Description,
            PurchaseVoucherDto.Of(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<PurchaseReturnDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnView);
        var ret = await LoadAsync(id, tracking: false, ct);
        var invoice = await db.PurchaseInvoices.Include(i => i.Lines).AsNoTracking().FirstAsync(i => i.Id == ret.InvoiceId, ct);
        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var supplier = await db.Parties.Where(s => s.Id == invoice.SupplierId).Select(s => s.DisplayName).FirstOrDefaultAsync(ct);
        var vouchers = await posting.FindAsync(PurchaseInvoice.Module, PurchaseReturn.DocType, [id], ct);
        var issueIds = ret.Lines.Where(l => l.IssueLineId is not null).Select(l => l.IssueLineId!.Value).ToList();
        var issues = await db.StockLines.Where(s => issueIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);

        return new PurchaseReturnDto(ret.Id, ret.DocNo, PersianDate.ToPersian(ret.DocDate), invoice.Id, invoice.DocNo,
            PersianDate.ToPersian(invoice.DocDate), invoice.SupplierId, supplier ?? "?", ret.Description, ret.Amount, ret.VatAmount, ret.Total,
            ret.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var il = invoice.Lines.First(x => x.Id == l.InvoiceLineId);
                var item = items.GetValueOrDefault(il.ItemId);
                return new PurchaseReturnLineDto(l.InvoiceLineId, il.RowNo, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "",
                    l.Quantity, l.Amount, l.VatAmount, l.IssueLineId, l.IssueLineId is long s ? issues.GetValueOrDefault(s) : null);
            }).ToList(),
            PurchaseVoucherDto.Of(vouchers.GetValueOrDefault(id)));
    }

    /// <summary>
    /// ردیف‌های حواله برگشت از خرید که از رسیدهای این فاکتور برگشت خورده و هنوز در برگشت از خرید نیامده‌اند
    /// (ردیف‌های همین برگشت در حال اصلاح هم می‌آیند).
    /// </summary>
    public async Task<List<PendingStockLineDto>> PendingIssuesAsync(long invoiceId, long? exceptReturnId, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnView);
        var invoice = await db.PurchaseInvoices.Include(i => i.Lines).AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException("فاکتور خرید پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        var byReceipt = invoice.Lines.Where(l => l.ReceiptLineId is not null).ToDictionary(l => l.ReceiptLineId!.Value);
        var receiptIds = byReceipt.Keys.ToList();
        var used = db.PurchaseReturns.Where(r => r.Id != (exceptReturnId ?? 0)).SelectMany(r => r.Lines)
            .Where(l => l.IssueLineId != null).Select(l => l.IssueLineId!.Value);
        var lines = await db.StockLines
            .Where(s => s.DocType == StockDocType.PurchaseReturnIssue && s.SourceLineId != null && receiptIds.Contains(s.SourceLineId.Value)
                        && !used.Contains(s.Id))
            .OrderBy(s => s.DocDate).ThenBy(s => s.DocNo).ThenBy(s => s.RowNo).ToListAsync(ct);
        var items = await db.Items.Where(i => invoice.Lines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return lines.Select(s =>
        {
            var il = byReceipt[s.SourceLineId!.Value];
            var item = items.GetValueOrDefault(s.ItemId);
            return new PendingStockLineDto(s.Id, s.DocId, s.DocType, s.DocNo, PersianDate.ToPersian(s.DocDate), s.WarehouseTitle, s.RowNo,
                s.ItemId, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "", s.Quantity, il.UnitPrice, il.Id);
        }).ToList();
    }

    /// <summary>ردیف‌های فاکتور با مانده قابل برگشت؛ برای برگشت در حال اصلاح، مقدار خودش کم نمی‌شود.</summary>
    public async Task<List<ReturnableLineDto>> ReturnableAsync(long invoiceId, long? exceptReturnId, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnView);
        var invoice = await db.PurchaseInvoices.Include(i => i.Lines).AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException("فاکتور خرید پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        var previous = await PreviousAsync(invoice, exceptReturnId, ct);
        var items = await db.Items.Where(i => invoice.Lines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return invoice.Lines.OrderBy(l => l.RowNo).Select(l =>
        {
            var p = previous.GetValueOrDefault(l.Id);
            var item = items.GetValueOrDefault(l.ItemId);
            return new ReturnableLineDto(l.Id, l.RowNo, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "", l.Quantity, l.UnitPrice,
                l.Amount, l.VatAmount, p.Quantity, l.Quantity - p.Quantity);
        }).ToList();
    }

    public async Task<long> CreateAsync(SavePurchaseReturnRequest request, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnCreate);
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.PurchaseReturns.MaxAsync(r => (int?)r.DocNo, ct) ?? 0) + 1;
        var ret = new PurchaseReturn(docNo, request.InvoiceId, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (invoice, supplier, items) = await ApplyAsync(ret, request, ct);
        db.Add(ret);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(ret, invoice, supplier, items), ct);
        await tx.CommitAsync(ct);
        return ret.Id;
    }

    public async Task UpdateAsync(long id, SavePurchaseReturnRequest request, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnEdit);
        await posting.EnsureChangeableAsync(PurchaseInvoice.Module, PurchaseReturn.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var ret = await LoadAsync(id, tracking: true, ct);
        if (ret.InvoiceId != request.InvoiceId) throw new DomainException("فاکتور مرجع برگشت عوض نمی‌شود؛ برگشت را حذف و دوباره ثبت کنید.");
        ret.ClearLines();
        await db.SaveChangesAsync(ct);
        var (invoice, supplier, items) = await ApplyAsync(ret, request, ct);
        ret.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(ret, invoice, supplier, items), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReturnDelete);
        await using var tx = await uow.BeginAsync(ct);
        var ret = await LoadAsync(id, tracking: true, ct);
        await posting.UnpostAsync(PurchaseInvoice.Module, PurchaseReturn.DocType, id, ct);
        db.Remove(ret);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task<Dictionary<long, (decimal Quantity, decimal Amount, decimal Vat)>> PreviousAsync(PurchaseInvoice invoice,
        long? exceptReturnId, CancellationToken ct)
    {
        var lineIds = invoice.Lines.Select(l => l.Id).ToList();
        var rows = await db.PurchaseReturns.Where(r => r.InvoiceId == invoice.Id && r.Id != (exceptReturnId ?? 0))
            .SelectMany(r => r.Lines).Where(l => lineIds.Contains(l.InvoiceLineId))
            .GroupBy(l => l.InvoiceLineId)
            .Select(g => new { g.Key, Q = g.Sum(l => l.Quantity), A = g.Sum(l => l.Amount), V = g.Sum(l => l.VatAmount) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => (r.Q, r.A, r.V));
    }

    private async Task<(PurchaseInvoice Invoice, PartyRef Supplier, Dictionary<int, ItemRef> Items)> ApplyAsync(PurchaseReturn ret,
        SavePurchaseReturnRequest request, CancellationToken ct)
    {
        var invoice = await db.PurchaseInvoices.Include(i => i.Lines).AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct)
                      ?? throw new DomainException("فاکتور خرید مرجع انتخاب نشده یا پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        var supplier = await db.Parties.FirstAsync(p => p.Id == invoice.SupplierId, ct);
        var items = await db.Items.Where(i => invoice.Lines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var previous = await PreviousAsync(invoice, ret.Id == 0 ? null : ret.Id, ct);

        // هر ردیف از یک ردیف حواله برگشت از خرید که از رسید همان ردیف فاکتور برگشت خورده، با همان مقدار
        var issueIds = request.Lines.Where(l => l.IssueLineId is not null).Select(l => l.IssueLineId!.Value).ToList();
        var issues = await db.StockLines.Where(s => issueIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var taken = await db.PurchaseReturns.Where(r => r.Id != ret.Id).SelectMany(r => r.Lines.Select(l => new { l.IssueLineId, r.DocNo }))
            .Where(x => x.IssueLineId != null && issueIds.Contains(x.IssueLineId.Value))
            .ToDictionaryAsync(x => x.IssueLineId!.Value, x => x.DocNo, ct);

        var inputs = new List<PurchaseReturnLineInput>();
        foreach (var r in request.Lines.Where(l => l.Quantity != 0))
        {
            var line = invoice.Lines.FirstOrDefault(l => l.Id == r.InvoiceLineId)
                       ?? throw new DomainException("ردیف برگشت در فاکتور خرید مرجع نیست.");
            var p = previous.GetValueOrDefault(line.Id);
            var title = items.GetValueOrDefault(line.ItemId)?.Title ?? "?";
            if (r.Quantity < 0) throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): مقدار برگشتی نمی‌تواند منفی باشد.");
            if (r.Quantity > line.Quantity - p.Quantity)
                throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): حداکثر {line.Quantity - p.Quantity:0.######} قابل برگشت است.");

            var issue = r.IssueLineId is long iid ? issues.GetValueOrDefault(iid) : null;
            if (issue is null || issue.DocType != StockDocType.PurchaseReturnIssue || issue.SourceLineId != line.ReceiptLineId)
                throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): برگشت از خرید باید از حواله برگشت از خرید همان رسید باشد " +
                                          "(اول حواله برگشت انبار، بعد برگشت از خرید).");
            if (taken.TryGetValue(issue.Id, out var other))
                throw new DomainException($"{issue.Title} قبلاً در برگشت از خرید {other:000000} آمده است.");
            if (issue.Quantity != r.Quantity)
                throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): مقدار برگشتی باید همان {issue.Title} ({issue.Quantity:0.######}) باشد.");

            inputs.Add(new PurchaseReturnLineInput(line.Id, r.Quantity,
                TradeMath.Share(line.Amount, line.Quantity, p.Quantity, p.Amount, r.Quantity),
                TradeMath.Share(line.VatAmount, line.Quantity, p.Quantity, p.Vat, r.Quantity), issue.Id));
            // دو ردیف انباری از یک ردیف فاکتور در یک برگشت: سهم دومی بعد از اولی حساب شود
            previous[line.Id] = (p.Quantity + r.Quantity, p.Amount + inputs[^1].Amount, p.Vat + inputs[^1].VatAmount);
        }

        ret.SetHeader(PersianDate.Parse(request.Date), invoice.DocDate, request.Description);
        ret.SetLines(inputs);
        return (invoice, supplier, items);
    }

    private static PostingRequest PostingRequest(PurchaseReturn ret, PurchaseInvoice invoice, PartyRef supplier, Dictionary<int, ItemRef> items)
    {
        var description = PurchaseInvoiceService.Cut($"برگشت از خرید {ret.DocNo} - فاکتور خرید {invoice.DocNo} - {supplier.DisplayName}"
                                                     + (ret.Description is { } d ? $" - {d}" : ""));
        var invoiceLines = invoice.Lines.ToDictionary(l => l.Id);
        var lines = new List<PostingLine>
        {
            new(invoice.PayableKind == PayableKind.Trade ? "payable_trade" : "payable_other", ret.Total,
                [new PostingDetail(PostingDetailKind.Person, supplier.Id)], $"برگشت از خرید {ret.DocNo}")
        };
        lines.AddRange(ret.Lines.GroupBy(l => items[invoiceLines[l.InvoiceLineId].ItemId].PurchaseAccountId!.Value)
            .Select(g => new PostingLine("inventory", g.Sum(l => l.Amount), [], $"برگشت کالا طبق برگشت از خرید {ret.DocNo}", AccountId: g.Key)));
        if (ret.VatAmount > 0)
            lines.Add(new PostingLine("vat", ret.VatAmount, [], $"ارزش افزوده برگشت از خرید {ret.DocNo}"));
        return new PostingRequest(PurchaseInvoice.Module, PurchaseReturn.DocType, ret.Id, ret.DocDate, description, lines);
    }

    private async Task<PurchaseReturn> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.PurchaseReturns.Include(r => r.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var ret = await query.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException($"برگشت از خرید با شناسه {id} پیدا نشد.");
        access.DemandOwner(ret.CreatedBy);
        return ret;
    }
}
