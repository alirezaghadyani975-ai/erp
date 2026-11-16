using Erp.SharedKernel.Security;
using Erp.Accounting.Contracts;
using Erp.Costing.Contracts;
using Erp.Expenses.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Expenses.Application;

/// <param name="VatAmount">ارزش افزوده ردیف (صفر = معاف).</param>
public sealed record ExpenseLineRequest(int CostFactorId, int? CostCenterId, decimal Amount, string? Description = null, decimal VatAmount = 0);

/// <summary>تاریخ‌ها شمسی، مثل 1405/07/21.</summary>
public sealed record SaveExpenseInvoiceRequest(
    string Date,
    int SupplierId,
    PayableKind PayableKind,
    string? InvoiceNo,
    string? InvoiceDate,
    string? Description,
    List<ExpenseLineRequest> Lines);

/// <summary>سند حسابداری فرم؛ وضعیت: 1 پیش‌نویس، 2 موقت، 3 دائم.</summary>
public sealed record ExpenseVoucherDto(long VoucherId, int TempNo, int? VoucherNo, byte Status);

public sealed record ExpenseInvoiceListItemDto(
    long Id, int DocNo, string Date, int SupplierId, string SupplierName, string? InvoiceNo,
    decimal LinesTotal, decimal VatAmount, decimal Total, string? Description, ExpenseVoucherDto? Voucher);

public sealed record ExpenseLineDto(short RowNo, int CostFactorId, string FactorTitle, int? CostCenterId, string? CenterTitle,
    decimal Amount, string? Description, decimal VatAmount);

public sealed record ExpenseInvoiceDto(
    long Id, int DocNo, string Date, int SupplierId, string SupplierName, PayableKind PayableKind,
    string? InvoiceNo, string? InvoiceDate, decimal VatAmount, string? Description,
    decimal LinesTotal, decimal Total, List<ExpenseLineDto> Lines, ExpenseVoucherDto? Voucher);

/// <summary>
/// شناسایی هزینه (فاکتور خدماتی). ثبت، اصلاح و حذف فرم و سند حسابداری‌اش در یک تراکنش انجام می‌شود:
/// اگر صدور سند خطا بدهد (مثلاً قاعده تفصیلی یا دوره بسته)، فرم هم ذخیره نمی‌شود.
/// </summary>
public class ExpenseInvoiceService(IExpensesDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user,
    TimeProvider clock, IAccessControl access)
{
    public async Task<List<ExpenseInvoiceListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(ExpensePermissions.View);
        var query = db.ExpenseInvoices.AsNoTracking();
        if (access.OwnDocsOnly) query = query.Where(i => i.CreatedBy == access.UserId);
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(i => i.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(i => i.DocDate <= t); }

        var rows = await query
            .Select(i => new
            {
                i.Id, i.DocNo, i.DocDate, i.SupplierId, i.InvoiceNo, i.VatAmount, i.Description,
                LinesTotal = i.Lines.Sum(l => l.Amount),
                Supplier = db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.DisplayName).FirstOrDefault()
            })
            .OrderByDescending(i => i.DocDate).ThenByDescending(i => i.DocNo)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Supplier ?? "").Contains(term) || (r.InvoiceNo ?? "").Contains(term)
                                   || (r.Description ?? "").Contains(term) || r.DocNo.ToString() == term).ToList();

        var vouchers = await posting.FindAsync(ExpenseInvoice.Module, ExpenseInvoice.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new ExpenseInvoiceListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.SupplierId,
            r.Supplier ?? "?", r.InvoiceNo, r.LinesTotal, r.VatAmount, r.LinesTotal + r.VatAmount, r.Description,
            ToDto(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<ExpenseInvoiceDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(ExpensePermissions.View);
        var invoice = await LoadAsync(id, tracking: false, ct);
        var factorIds = invoice.Lines.Select(l => l.CostFactorId).Distinct().ToList();
        var centerIds = invoice.Lines.Select(l => l.CostCenterId).OfType<int>().Distinct().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var supplier = await db.Suppliers.Where(s => s.Id == invoice.SupplierId).Select(s => s.DisplayName).FirstOrDefaultAsync(ct);
        var vouchers = await posting.FindAsync(ExpenseInvoice.Module, ExpenseInvoice.DocType, [id], ct);

        return new ExpenseInvoiceDto(invoice.Id, invoice.DocNo, PersianDate.ToPersian(invoice.DocDate), invoice.SupplierId,
            supplier ?? "?", invoice.PayableKind, invoice.InvoiceNo,
            invoice.InvoiceDate is { } d ? PersianDate.ToPersian(d) : null, invoice.VatAmount, invoice.Description,
            invoice.LinesTotal, invoice.Total,
            invoice.Lines.OrderBy(l => l.RowNo).Select(l => new ExpenseLineDto(l.RowNo, l.CostFactorId,
                factors.GetValueOrDefault(l.CostFactorId, "?"), l.CostCenterId,
                l.CostCenterId is int c ? centers.GetValueOrDefault(c) : null, l.Amount, l.Description, l.VatAmount)).ToList(),
            ToDto(vouchers.GetValueOrDefault(id)));
    }

    /// <summary>ثبت فرم و صدور سند حسابداری موقت آن.</summary>
    public async Task<long> CreateAsync(SaveExpenseInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(ExpensePermissions.Create);
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.ExpenseInvoices.MaxAsync(i => (int?)i.DocNo, ct) ?? 0) + 1;
        var invoice = new ExpenseInvoice(docNo, user.UserId, clock.GetUtcNow().UtcDateTime);
        var supplier = await ApplyAsync(invoice, request, ct);
        db.Add(invoice);
        await db.SaveChangesAsync(ct);

        await posting.PostAsync(await PostingRequestAsync(invoice, supplier, ct), ct);
        await tx.CommitAsync(ct);
        return invoice.Id;
    }

    /// <summary>اصلاح فرم؛ سند حسابداری‌اش با همان شماره بازنویسی می‌شود (اگر دائم نشده باشد).</summary>
    public async Task UpdateAsync(long id, SaveExpenseInvoiceRequest request, CancellationToken ct)
    {
        access.Demand(ExpensePermissions.Edit);
        await posting.EnsureChangeableAsync(ExpenseInvoice.Module, ExpenseInvoice.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);

        invoice.ClearLines();
        await db.SaveChangesAsync(ct); // ردیف‌های قبلی پیش از درج ردیف‌های تازه حذف شوند
        var supplier = await ApplyAsync(invoice, request, ct);
        invoice.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        await posting.PostAsync(await PostingRequestAsync(invoice, supplier, ct), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>حذف فرم و سند حسابداری‌اش (اگر دائم نشده باشد).</summary>
    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(ExpensePermissions.Delete);
        await using var tx = await uow.BeginAsync(ct);
        var invoice = await LoadAsync(id, tracking: true, ct);
        await posting.UnpostAsync(ExpenseInvoice.Module, ExpenseInvoice.DocType, id, ct);
        db.Remove(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task<SupplierRef> ApplyAsync(ExpenseInvoice invoice, SaveExpenseInvoiceRequest request, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
                       ?? throw new DomainException("تأمین‌کننده (طرف حساب) انتخاب نشده یا پیدا نشد.");
        if (!supplier.IsActive && supplier.Id != invoice.SupplierId)
            throw new DomainException($"شخص «{supplier.DisplayName}» غیرفعال است.");

        var factorIds = request.Lines.Select(l => l.CostFactorId).Distinct().ToList();
        var centerIds = request.Lines.Select(l => l.CostCenterId).OfType<int>().Distinct().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var row = 0;
        foreach (var line in request.Lines)
        {
            row++;
            var factor = factors.GetValueOrDefault(line.CostFactorId)
                         ?? throw new DomainException($"ردیف {row}: عامل هزینه انتخاب نشده یا پیدا نشد.");
            if (!factor.IsActive) throw new DomainException($"ردیف {row}: عامل «{factor.Title}» غیرفعال است.");
            if (factor.Nature != CostNature.Finance)
            {
                if (line.CostCenterId is not int cid)
                    throw new DomainException($"ردیف {row}: مرکز هزینه برای «{factor.Title}» الزامی است.");
                var center = centers.GetValueOrDefault(cid) ?? throw new DomainException($"ردیف {row}: مرکز هزینه پیدا نشد.");
                if (!center.IsActive) throw new DomainException($"ردیف {row}: مرکز «{center.Title}» غیرفعال است.");
            }
        }

        var date = PersianDate.Parse(request.Date);
        DateOnly? invoiceDate = string.IsNullOrWhiteSpace(request.InvoiceDate) ? null : PersianDate.Parse(request.InvoiceDate);
        invoice.SetHeader(date, supplier.Id, request.PayableKind, request.InvoiceNo, invoiceDate, request.Description);
        // هزینه مالی مرکز ندارد؛ مرکز انتخاب‌شده برایش نگه داشته نمی‌شود
        invoice.SetLines(request.Lines.Select(l => (l.CostFactorId,
            factors[l.CostFactorId].Nature == CostNature.Finance ? null : l.CostCenterId, l.Amount, l.VatAmount, l.Description)));

        if (invoice.InvoiceNo is { } no
            && await db.ExpenseInvoices.AnyAsync(i => i.Id != invoice.Id && i.SupplierId == supplier.Id && i.InvoiceNo == no, ct))
        {
            var other = await db.ExpenseInvoices.Where(i => i.Id != invoice.Id && i.SupplierId == supplier.Id && i.InvoiceNo == no)
                .Select(i => i.DocNo).FirstAsync(ct);
            throw new DomainException($"فاکتور شماره {no} از «{supplier.DisplayName}» قبلاً در فرم شماره {other} ثبت شده است.");
        }
        return supplier;
    }

    private async Task<PostingRequest> PostingRequestAsync(ExpenseInvoice invoice, SupplierRef supplier, CancellationToken ct)
    {
        var factorIds = invoice.Lines.Select(l => l.CostFactorId).Distinct().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Title, ct);

        var description = $"شناسایی هزینه {invoice.DocNo} - {supplier.DisplayName}"
                          + (invoice.InvoiceNo is { } no ? $" - فاکتور {no}" : "")
                          + (invoice.Description is { } d ? $" - {d}" : "");
        if (description.Length > 500) description = description[..500];

        var lines = invoice.Lines.OrderBy(l => l.RowNo).Select(l => new PostingLine("expense", l.Amount,
            l.CostCenterId is int c
                ? [new PostingDetail(PostingDetailKind.CostFactor, l.CostFactorId), new PostingDetail(PostingDetailKind.CostCenter, c)]
                : [new PostingDetail(PostingDetailKind.CostFactor, l.CostFactorId)],
            l.Description ?? factors.GetValueOrDefault(l.CostFactorId))).ToList();
        if (invoice.VatAmount > 0)
            lines.Add(new PostingLine("vat", invoice.VatAmount, [], $"ارزش افزوده شناسایی هزینه {invoice.DocNo}"));
        lines.Add(new PostingLine(invoice.PayableKind == PayableKind.Trade ? "payable_trade" : "payable_other", invoice.Total,
            [new PostingDetail(PostingDetailKind.Person, supplier.Id)],
            invoice.InvoiceNo is { } inv ? $"فاکتور {inv}" : null));

        return new PostingRequest(ExpenseInvoice.Module, ExpenseInvoice.DocType, invoice.Id, invoice.DocDate, description, lines);
    }

    private async Task<ExpenseInvoice> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.ExpenseInvoices.Include(i => i.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var invoice = await query.FirstOrDefaultAsync(i => i.Id == id, ct)
               ?? throw new NotFoundException($"شناسایی هزینه با شناسه {id} پیدا نشد.");
        access.DemandOwner(invoice.CreatedBy);
        return invoice;
    }

    private static ExpenseVoucherDto? ToDto(PostedVoucher? v) =>
        v is null ? null : new ExpenseVoucherDto(v.VoucherId, v.TempNo, v.VoucherNo, v.Status);
}
