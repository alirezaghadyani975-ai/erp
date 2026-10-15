using Erp.Accounting.Domain.Details;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class DetailTypeConfiguration : IEntityTypeConfiguration<DetailType>
{
    public void Configure(EntityTypeBuilder<DetailType> b)
    {
        b.ToTable("DetailTypes", "fin", t => t.HasTrigger("trg_DetailTypes_Overlap"));
        b.HasKey(t => t.Id);
        b.Property(t => t.Title).HasMaxLength(100);
        b.Property(t => t.OwnerModule).HasMaxLength(30).IsUnicode(false);
        b.Property(t => t.CodeFrom).HasMaxLength(10).IsUnicode(false);
        b.Property(t => t.CodeTo).HasMaxLength(10).IsUnicode(false);
        b.Property(t => t.SystemKey).HasMaxLength(30).IsUnicode(false);
        b.Ignore(t => t.CodeLength);
        b.Ignore(t => t.DomainEvents);
    }
}

internal class DetailAccountConfiguration : IEntityTypeConfiguration<DetailAccount>
{
    public void Configure(EntityTypeBuilder<DetailAccount> b)
    {
        b.ToTable("DetailAccounts", "fin", t => t.HasTrigger("trg_DetailAccounts_Range"));
        b.HasKey(d => d.Id);
        b.Property(d => d.Code).HasMaxLength(10).IsUnicode(false);
        b.Property(d => d.Title).HasMaxLength(200);
        b.Property(d => d.SourceModule).HasMaxLength(30).IsUnicode(false);
        b.HasOne<DetailType>().WithMany().HasForeignKey(d => d.DetailTypeId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(d => d.DomainEvents);
    }
}
