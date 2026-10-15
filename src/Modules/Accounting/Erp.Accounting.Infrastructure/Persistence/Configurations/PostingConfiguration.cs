using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Accounting.Infrastructure.Persistence.Configurations;

internal class PostingTemplateConfiguration : IEntityTypeConfiguration<PostingTemplate>
{
    public void Configure(EntityTypeBuilder<PostingTemplate> b)
    {
        b.ToTable("PostingTemplates", "fin");
        b.HasKey(t => t.Id);
        b.Property(t => t.SourceDocType).HasMaxLength(30).IsUnicode(false);
        b.Property(t => t.SourceModule).HasMaxLength(30).IsUnicode(false);
        b.Property(t => t.Title).HasMaxLength(100);
        b.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TemplateId);
        b.Navigation(t => t.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(t => t.DomainEvents);
    }
}

internal class PostingTemplateLineConfiguration : IEntityTypeConfiguration<PostingTemplateLine>
{
    public void Configure(EntityTypeBuilder<PostingTemplateLine> b)
    {
        b.ToTable("PostingTemplateLines", "fin");
        b.HasKey(l => l.Id);
        b.Property(l => l.LineKey).HasMaxLength(30).IsUnicode(false);
        b.Property(l => l.Title).HasMaxLength(100);
        b.Property(l => l.AccountRule).HasMaxLength(30).IsUnicode(false);
        b.Property(l => l.DetailHint).HasMaxLength(200);
        b.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
