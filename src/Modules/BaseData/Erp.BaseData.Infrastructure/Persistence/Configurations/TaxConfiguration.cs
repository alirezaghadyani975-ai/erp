using Erp.BaseData.Domain.Taxes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.BaseData.Infrastructure.Persistence.Configurations;

internal class VatRateConfiguration : IEntityTypeConfiguration<VatRate>
{
    public void Configure(EntityTypeBuilder<VatRate> b)
    {
        b.ToTable("VatRates", "base");
        b.HasKey(x => x.Id);
        b.Property(x => x.TaxPercent).HasPrecision(5, 2);
        b.Property(x => x.TollPercent).HasPrecision(5, 2);
        b.Ignore(x => x.TotalPercent);
    }
}
