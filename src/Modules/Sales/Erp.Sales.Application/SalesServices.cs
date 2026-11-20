using Erp.Accounting.Contracts;
using Erp.Inventory.Contracts;
using Erp.Sales.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Sales.Application;

/// <summary>
/// فاکتور فروش. فرم و سند حسابداری‌اش در یک تراکنش ثبت، اصلاح و حذف می‌شوند. فاکتوری که برگشت دارد یا به سامانه
/// مودیان ارسال شده اصلاح و حذف نمی‌شود. دریافت وجه از خزانه‌داری ثبت می‌شود.
/// </summary>
public class SalesInvoiceService(ISalesDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user,
    TimeProvider clock, IAccessControl access)
{
    public async Task<List<SalesInvoiceListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(SalesPermissions.InvoiceView);
        var query = db.SalesInvoices.AsNoTracking();
        if (access.OwnDocsOnly) query = query.Where(i => i.CreatedBy == access.UserId);
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(i => i.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(i => i.DocDate <= t); }

        var rows = await query
            .Select(i => new
            {
                i.Id, i.DocNo, i.DocDate, i.CustomerId, i.GrossAmount, i.DiscountAmount, i.VatAmount, i.SettlementMethod, i.Description,
                Customer = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.DisplayName).FirstOrDefault()
            })
            .OrderByDescending(i => i.DocDate).ThenByDescending(i => i.DocNo)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Customer ?? "").Contains(term) || (r.Description ?? "").Contains(term) || r.DocNo.ToString() == term).ToList();

        var vouchers = await posting.FindAsync(SalesInvoice.Module, SalesInvoice.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new SalesInvoiceListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.CustomerId, r.Customer ?? "?",
            r.GrossAmount, r.DiscountAmount, r.VatAmount, r.GrossAmount - r.DiscountAmount + r.VatAmount, r.SettlementMethod, r.Description,
            SalesVoucherDto.Of(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<SalesInvoiceDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(SalesPermissions.InvoiceView);
        var invoice = await LoadAsync(id, tracking: false, ct);
        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var lineIds = invoice.Lines.Select(l => l.Id).ToList();
        var returned = await db.SalesReturns.SelectMany(r => r.Lines).Where(l => lineIds.Contains(l.InvoiceLineId))
            .GroupBy(l => l.InvoiceLineId).Select(g => new { g.Key, Qty = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var returnCount = await db.SalesReturns.CountAsync(r => r.InvoiceId == id, ct);
        var customer = await db.Customers.Where(c => c.Id == invoice.CustomerId).Select(c => c.DisplayName).FirstOrDefaultAsync(ct);
        var project = invoice.ProjectDetailId is int pid
            ? await db.Details.Where(d => d.Id == pid).Select(d => d.Code + " - " + d.Title).FirstOrDefaultAsync(ct)
            : null;
        var vouchers = await posting.FindAsync(SalesInvoice.Module, SalesInvoice.DocType, [id], ct);

        return new SalesInvoiceDto(invoice.Id, invoice.DocNo, PersianDate.ToPersian(invoice.DocDate), invoice.CustomerId, customer ?? "?",
            invoice.InvoiceType, invoice.SettlementMethod, invoice.CashAmount, invoice.ProjectDetailId, project, invoice.Description,
            invoice.GrossAmount, invoice.DiscountAmount, invoice.NetAmount, invoice.VatAmount, invoice.Total, invoice.TaxStatus,
            invoice.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var item = items.GetValueOrDefault(l.ItemId);
                return new SalesLineDto(l.Id, l.RowNo, l.ItemId, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "",
                    l.Quantity, l.UnitPrice, l.Discount, l.Amount, l.VatAmount, l.Description, returned.GetValueOrDefault(l.Id));
            }).ToList(),
            SalesVoucherDto.Of(vouchers.GetValueOrDefault(id)), returnCount);
    }

    public async Task<long> CreateAsync(SaveSalesInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(SalesPermissions.InvoiceCreate);
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.SalesInvoices.MaxAsync(i => (int?)i.DocNo, ct) ?? 0) + 1;
        var invoice = new SalesInvoice(docNo, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (customer, items) = await ApplyAsync(invoice, request, ct);
        db.Add(invoice);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(invoice, customer, items), ct);
        await tx.CommitAsync(ct);
        return invoice.Id;
    }

    public async Task UpdateAsync(long id, SaveSalesInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(SalesPermissions.InvoiceEdit);
        await posting.EnsureChangeableAsync(SalesInvoice.Module, SalesInvoice.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);
        await EnsureChangeableAsync(invoice, "اصلاح", ct);

        invoice.ClearLines();
        await db.SaveChangesAsync(ct);
        var (customer, items) = await ApplyAsync(invoice, request, ct);
        invoice.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(invoice, customer, items), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(SalesPermissions.InvoiceDelete);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);
        await EnsureChangeableAsync(invoice, "حذف", ct);
        await posting.UnpostAsync(SalesInvoice.Module, SalesInvoice.DocType, id, ct);
        db.Remove(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task EnsureChangeableAsync(SalesInvoice invoice, string action, CancellationToken ct)
    {
        if (invoice.TaxStatus != TaxStatus.NotSent)
            throw new DomainException($"فاکتور فروش {invoice.DocNo} به سامانه مودیان ارسال شده و {action} نمی‌شود؛ صورتحساب اصلاحی یا ابطال لازم است.");
        var returns = await db.SalesReturns.Where(r => r.InvoiceId == invoice.Id).Select(r => r.DocNo).ToListAsync(ct);
        if (returns.Count > 0)
            throw new DomainException($"فاکتور فروش {invoice.DocNo} برگشت دارد (برگشت شماره {string.Join("، ", returns)}) و {action} نمی‌شود؛ " +
                                      "اول برگشت‌ها را حذف کنید.");
    }

    private async Task<(CustomerRef Customer, Dictionary<int, SalesItemRef> Items)> ApplyAsync(SalesInvoice invoice,
        SaveSalesInvoiceRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new DomainException("مشتری انتخاب نشده یا پیدا نشد.");
        if (!customer.IsActive && customer.Id != invoice.CustomerId)
            throw new DomainException($"شخص «{customer.DisplayName}» غیرفعال است.");

        if (request.ProjectDetailId is int pid)
        {
            var project = await db.Details.FirstOrDefaultAsync(d => d.Id == pid, ct) ?? throw new DomainException("پروژه انتخاب‌شده پیدا نشد.");
            if (!project.IsActive && pid != invoice.ProjectDetailId) throw new DomainException($"تفصیلی «{project.Title}» غیرفعال است.");
        }

        var previous = invoice.Lines.Select(l => l.ItemId).ToHashSet();
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var row = 0;
        foreach (var line in request.Lines)
        {
            row++;
            var item = items.GetValueOrDefault(line.ItemId) ?? throw new DomainException($"ردیف {row}: کالا انتخاب نشده یا پیدا نشد.");
            if (!item.IsActive && !previous.Contains(item.Id)) throw new DomainException($"ردیف {row}: «{item.Title}» غیرفعال است.");
            if (item.VatExempt && line.VatAmount != 0) throw new DomainException($"ردیف {row}: «{item.Title}» معاف از ارزش افزوده است.");
        }

        invoice.SetHeader(PersianDate.Parse(request.Date), customer.Id, request.InvoiceType, request.Settlement, request.ProjectDetailId,
            request.Description);
        invoice.SetLines(request.Lines.Select(l => new SalesLineInput(l.ItemId, l.Quantity, l.UnitPrice, l.Discount, l.VatAmount, l.Description)),
            request.CashAmount);
        return (customer, items);
    }

    private static PostingRequest PostingRequest(SalesInvoice invoice, CustomerRef customer, Dictionary<int, SalesItemRef> items)
    {
        var description = Cut($"فاکتور فروش {invoice.DocNo} - {customer.DisplayName}" + (invoice.Description is { } d ? $" - {d}" : ""));
        IReadOnlyList<PostingDetail> project = invoice.ProjectDetailId is int p ? [new PostingDetail(PostingDetailKind.DetailAccount, p)] : [];

        var lines = new List<PostingLine>
        {
            new("receivable", invoice.Total, [new PostingDetail(PostingDetailKind.Person, customer.Id)], $"فاکتور فروش {invoice.DocNo}")
        };
        if (invoice.DiscountAmount > 0)
            lines.Add(new PostingLine("discount", invoice.DiscountAmount, [], $"تخفیف فاکتور فروش {invoice.DocNo}"));
        lines.AddRange(invoice.Lines.GroupBy(l => items[l.ItemId].SalesAccountId)
            .Select(g => new PostingLine("revenue", g.Sum(l => l.GrossAmount), project, $"فروش طبق فاکتور {invoice.DocNo}", AccountId: g.Key)));
        if (invoice.VatAmount > 0)
            lines.Add(new PostingLine("vat", invoice.VatAmount, [], $"ارزش افزوده فاکتور فروش {invoice.DocNo}"));
        return new PostingRequest(SalesInvoice.Module, SalesInvoice.DocType, invoice.Id, invoice.DocDate, description, lines);
    }

    private async Task<SalesInvoice> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.SalesInvoices.Include(i => i.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var invoice = await query.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException($"فاکتور فروش با شناسه {id} پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        return invoice;
    }

    internal static string Cut(string text) => text.Length > 500 ? text[..500] : text;
}

/// <summary>
/// برگشت از فروش با ارجاع به فاکتور فروش. مبلغ ناخالص، تخفیف و ارزش افزوده هر ردیف به نسبت مقدار از ردیف فاکتور
/// حساب می‌شود (آخرین برگشت یک ردیف باقی مبلغ را می‌گیرد).
/// </summary>
public class SalesReturnService(ISalesDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user,
    TimeProvider clock, IAccessControl access)
{
    public async Task<List<SalesReturnListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnView);
        var query = db.SalesReturns.AsNoTracking();
        if (access.OwnDocsOnly) query = query.Where(r => r.CreatedBy == access.UserId);
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }

        var rows = await query
            .Select(r => new
            {
                r.Id, r.DocNo, r.DocDate, r.InvoiceId, r.GrossAmount, r.DiscountAmount, r.VatAmount, r.Description,
                Invoice = db.SalesInvoices.Where(i => i.Id == r.InvoiceId).Select(i => new
                {
                    i.DocNo,
                    Customer = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.DisplayName).FirstOrDefault()
                }).First()
            })
            .OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Invoice.Customer ?? "").Contains(term) || (r.Description ?? "").Contains(term)
                                   || r.DocNo.ToString() == term || r.Invoice.DocNo.ToString() == term).ToList();

        var vouchers = await posting.FindAsync(SalesInvoice.Module, SalesReturn.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new SalesReturnListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.InvoiceId, r.Invoice.DocNo,
            r.Invoice.Customer ?? "?", r.GrossAmount - r.DiscountAmount, r.VatAmount, r.GrossAmount - r.DiscountAmount + r.VatAmount,
            r.Description, SalesVoucherDto.Of(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<SalesReturnDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnView);
        var ret = await LoadAsync(id, tracking: false, ct);
        var invoice = await db.SalesInvoices.Include(i => i.Lines).AsNoTracking().FirstAsync(i => i.Id == ret.InvoiceId, ct);
        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var customer = await db.Customers.Where(c => c.Id == invoice.CustomerId).Select(c => c.DisplayName).FirstOrDefaultAsync(ct);
        var vouchers = await posting.FindAsync(SalesInvoice.Module, SalesReturn.DocType, [id], ct);

        return new SalesReturnDto(ret.Id, ret.DocNo, PersianDate.ToPersian(ret.DocDate), invoice.Id, invoice.DocNo,
            PersianDate.ToPersian(invoice.DocDate), invoice.CustomerId, customer ?? "?", ret.Description, ret.GrossAmount, ret.DiscountAmount,
            ret.VatAmount, ret.Total,
            ret.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var il = invoice.Lines.First(x => x.Id == l.InvoiceLineId);
                var item = items.GetValueOrDefault(il.ItemId);
                return new SalesReturnLineDto(l.InvoiceLineId, il.RowNo, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "",
                    l.Quantity, l.GrossAmount, l.Discount, l.Amount, l.VatAmount);
            }).ToList(),
            SalesVoucherDto.Of(vouchers.GetValueOrDefault(id)));
    }

    public async Task<List<SalesReturnableLineDto>> ReturnableAsync(long invoiceId, long? exceptReturnId, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnView);
        var invoice = await db.SalesInvoices.Include(i => i.Lines).AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException("فاکتور فروش پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        var previous = await PreviousAsync(invoice, exceptReturnId, ct);
        var items = await db.Items.Where(i => invoice.Lines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return invoice.Lines.OrderBy(l => l.RowNo).Select(l =>
        {
            var p = previous.GetValueOrDefault(l.Id) ?? new Prev(0, 0, 0, 0);
            var item = items.GetValueOrDefault(l.ItemId);
            return new SalesReturnableLineDto(l.Id, l.RowNo, item?.Code ?? "?", item?.Title ?? "?", item?.UnitTitle ?? "", l.Quantity,
                l.UnitPrice, l.Amount, l.VatAmount, p.Quantity, l.Quantity - p.Quantity);
        }).ToList();
    }

    public async Task<long> CreateAsync(SaveSalesReturnRequest request, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnCreate);
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.SalesReturns.MaxAsync(r => (int?)r.DocNo, ct) ?? 0) + 1;
        var ret = new SalesReturn(docNo, request.InvoiceId, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (invoice, customer, items) = await ApplyAsync(ret, request, ct);
        db.Add(ret);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(ret, invoice, customer, items), ct);
        await tx.CommitAsync(ct);
        return ret.Id;
    }

    public async Task UpdateAsync(long id, SaveSalesReturnRequest request, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnEdit);
        await posting.EnsureChangeableAsync(SalesInvoice.Module, SalesReturn.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var ret = await LoadAsync(id, tracking: true, ct);
        if (ret.InvoiceId != request.InvoiceId) throw new DomainException("فاکتور مرجع برگشت عوض نمی‌شود؛ برگشت را حذف و دوباره ثبت کنید.");
        if (ret.TaxStatus != TaxStatus.NotSent) throw new DomainException("این برگشت به سامانه مودیان ارسال شده و اصلاح نمی‌شود.");
        ret.ClearLines();
        await db.SaveChangesAsync(ct);
        var (invoice, customer, items) = await ApplyAsync(ret, request, ct);
        ret.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(ret, invoice, customer, items), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReturnDelete);
        await using var tx = await uow.BeginAsync(ct);
        var ret = await LoadAsync(id, tracking: true, ct);
        if (ret.TaxStatus != TaxStatus.NotSent) throw new DomainException("این برگشت به سامانه مودیان ارسال شده و حذف نمی‌شود.");
        await posting.UnpostAsync(SalesInvoice.Module, SalesReturn.DocType, id, ct);
        db.Remove(ret);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private sealed record Prev(decimal Quantity, decimal Gross, decimal Discount, decimal Vat);

    private async Task<Dictionary<long, Prev>> PreviousAsync(SalesInvoice invoice, long? exceptReturnId, CancellationToken ct)
    {
        var lineIds = invoice.Lines.Select(l => l.Id).ToList();
        var rows = await db.SalesReturns.Where(r => r.InvoiceId == invoice.Id && r.Id != (exceptReturnId ?? 0))
            .SelectMany(r => r.Lines).Where(l => lineIds.Contains(l.InvoiceLineId))
            .GroupBy(l => l.InvoiceLineId)
            .Select(g => new { g.Key, Q = g.Sum(l => l.Quantity), G = g.Sum(l => l.GrossAmount), D = g.Sum(l => l.Discount), V = g.Sum(l => l.VatAmount) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => new Prev(r.Q, r.G, r.D, r.V));
    }

    private async Task<(SalesInvoice Invoice, CustomerRef Customer, Dictionary<int, SalesItemRef> Items)> ApplyAsync(SalesReturn ret,
        SaveSalesReturnRequest request, CancellationToken ct)
    {
        var invoice = await db.SalesInvoices.Include(i => i.Lines).AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct)
                      ?? throw new DomainException("فاکتور فروش مرجع انتخاب نشده یا پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        var customer = await db.Customers.FirstAsync(c => c.Id == invoice.CustomerId, ct);
        var items = await db.Items.Where(i => invoice.Lines.Select(l => l.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var previous = await PreviousAsync(invoice, ret.Id == 0 ? null : ret.Id, ct);
        var none = new Prev(0, 0, 0, 0);

        var inputs = new List<SalesReturnLineInput>();
        foreach (var r in request.Lines.Where(l => l.Quantity != 0))
        {
            var line = invoice.Lines.FirstOrDefault(l => l.Id == r.InvoiceLineId) ?? throw new DomainException("ردیف برگشت در فاکتور فروش مرجع نیست.");
            var p = previous.GetValueOrDefault(line.Id) ?? none;
            var title = items.GetValueOrDefault(line.ItemId)?.Title ?? "?";
            if (r.Quantity < 0) throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): مقدار برگشتی نمی‌تواند منفی باشد.");
            if (r.Quantity > line.Quantity - p.Quantity)
                throw new DomainException($"ردیف {line.RowNo} فاکتور («{title}»): حداکثر {line.Quantity - p.Quantity:0.######} قابل برگشت است.");
            inputs.Add(new SalesReturnLineInput(line.Id, r.Quantity,
                TradeMath.Share(line.GrossAmount, line.Quantity, p.Quantity, p.Gross, r.Quantity),
                TradeMath.Share(line.Discount, line.Quantity, p.Quantity, p.Discount, r.Quantity),
                TradeMath.Share(line.VatAmount, line.Quantity, p.Quantity, p.Vat, r.Quantity)));
        }

        ret.SetHeader(PersianDate.Parse(request.Date), invoice.DocDate, request.Description);
        ret.SetLines(inputs);
        return (invoice, customer, items);
    }

    private static PostingRequest PostingRequest(SalesReturn ret, SalesInvoice invoice, CustomerRef customer, Dictionary<int, SalesItemRef> items)
    {
        var description = SalesInvoiceService.Cut($"برگشت از فروش {ret.DocNo} - فاکتور فروش {invoice.DocNo} - {customer.DisplayName}"
                                                  + (ret.Description is { } d ? $" - {d}" : ""));
        IReadOnlyList<PostingDetail> project = invoice.ProjectDetailId is int p ? [new PostingDetail(PostingDetailKind.DetailAccount, p)] : [];
        var invoiceLines = invoice.Lines.ToDictionary(l => l.Id);

        var lines = ret.Lines.GroupBy(l => items[invoiceLines[l.InvoiceLineId].ItemId].SalesReturnAccountId)
            .Select(g => new PostingLine("return", g.Sum(l => l.GrossAmount), project, $"برگشت از فروش {ret.DocNo}", AccountId: g.Key))
            .ToList();
        if (ret.VatAmount > 0)
            lines.Add(new PostingLine("vat", ret.VatAmount, [], $"ارزش افزوده برگشت از فروش {ret.DocNo}"));
        if (ret.DiscountAmount > 0)
            lines.Add(new PostingLine("discount", ret.DiscountAmount, [], $"تخفیف برگشت از فروش {ret.DocNo}"));
        lines.Add(new PostingLine("receivable", ret.Total, [new PostingDetail(PostingDetailKind.Person, customer.Id)], $"برگشت از فروش {ret.DocNo}"));
        return new PostingRequest(SalesInvoice.Module, SalesReturn.DocType, ret.Id, ret.DocDate, description, lines);
    }

    private async Task<SalesReturn> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.SalesReturns.Include(r => r.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var ret = await query.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException($"برگشت از فروش با شناسه {id} پیدا نشد.");
        access.DemandOwner(ret.CreatedBy);
        return ret;
    }
}
