using Erp.BaseData.Domain.Persons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.BaseData.Infrastructure.Persistence.Configurations;

internal class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("Persons", "base");
        b.HasKey(p => p.Id);
        b.Property(p => p.Kind).HasColumnName("PersonKind");
        b.Property(p => p.DisplayName).HasMaxLength(200);
        b.Property(p => p.NationalId).HasMaxLength(12).IsUnicode(false);
        b.Property(p => p.EconomicCode).HasMaxLength(20).IsUnicode(false);
        b.Property(p => p.FirstName).HasMaxLength(100);
        b.Property(p => p.LastName).HasMaxLength(100);
        b.Property(p => p.FatherName).HasMaxLength(100);
        b.Property(p => p.IdNumber).HasMaxLength(10).IsUnicode(false);
        b.Property(p => p.ForeignNationalCode).HasMaxLength(12).IsUnicode(false);
        b.Property(p => p.PassportNo).HasMaxLength(20).IsUnicode(false);
        b.Property(p => p.RegistrationNo).HasMaxLength(30);
        b.Property(p => p.BranchCode).HasMaxLength(4).IsUnicode(false);
        b.Property(p => p.Mobile).HasMaxLength(11).IsUnicode(false);
        b.Property(p => p.Phone).HasMaxLength(11).IsUnicode(false);
        b.Property(p => p.Email).HasMaxLength(100).IsUnicode(false);
        b.Property(p => p.Website).HasMaxLength(200).IsUnicode(false);
        b.Property(p => p.Notes).HasMaxLength(500);
        b.Property(p => p.CreatedAt).ValueGeneratedOnAdd();

        b.HasMany(p => p.Roles).WithOne().HasForeignKey(r => r.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.Addresses).WithOne().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.BankAccounts).WithOne().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.BankAccounts).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.Contacts).WithOne().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(p => p.DomainEvents);
    }
}

internal class PersonRoleConfiguration : IEntityTypeConfiguration<PersonRole>
{
    public void Configure(EntityTypeBuilder<PersonRole> b)
    {
        b.ToTable("PersonRoles", "base");
        b.HasKey(r => new { r.PersonId, r.RoleId });
        b.HasOne<PersonRoleType>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class PersonRoleTypeConfiguration : IEntityTypeConfiguration<PersonRoleType>
{
    public void Configure(EntityTypeBuilder<PersonRoleType> b)
    {
        b.ToTable("PersonRoleTypes", "base");
        b.HasKey(r => r.Id);
        b.Property(r => r.Code).HasMaxLength(30).IsUnicode(false);
        b.Property(r => r.Title).HasMaxLength(50);
    }
}

internal class PersonAddressConfiguration : IEntityTypeConfiguration<PersonAddress>
{
    public void Configure(EntityTypeBuilder<PersonAddress> b)
    {
        b.ToTable("PersonAddresses", "base");
        b.HasKey(a => a.Id);
        b.Property(a => a.Address).HasMaxLength(400);
        b.Property(a => a.PostalCode).HasMaxLength(10).IsUnicode(false);
        b.Property(a => a.Phone).HasMaxLength(11).IsUnicode(false);
    }
}

internal class PersonBankAccountConfiguration : IEntityTypeConfiguration<PersonBankAccount>
{
    public void Configure(EntityTypeBuilder<PersonBankAccount> b)
    {
        b.ToTable("PersonBankAccounts", "base");
        b.HasKey(a => a.Id);
        b.Property(a => a.Iban).HasMaxLength(26).IsUnicode(false);
        b.Property(a => a.AccountNo).HasMaxLength(30).IsUnicode(false);
        b.Property(a => a.CardNo).HasMaxLength(16).IsUnicode(false);
    }
}

internal class PersonContactConfiguration : IEntityTypeConfiguration<PersonContact>
{
    public void Configure(EntityTypeBuilder<PersonContact> b)
    {
        b.ToTable("PersonContacts", "base");
        b.HasKey(c => c.Id);
        b.Property(c => c.FullName).HasMaxLength(150);
        b.Property(c => c.Mobile).HasMaxLength(11).IsUnicode(false);
        b.Property(c => c.Phone).HasMaxLength(11).IsUnicode(false);
        b.Property(c => c.Email).HasMaxLength(100).IsUnicode(false);
        b.Property(c => c.Notes).HasMaxLength(300);
    }
}

internal class PersonAttachmentConfiguration : IEntityTypeConfiguration<PersonAttachment>
{
    public void Configure(EntityTypeBuilder<PersonAttachment> b)
    {
        b.ToTable("PersonAttachments", "base");
        b.HasKey(a => a.Id);
        b.Property(a => a.Title).HasMaxLength(150);
        b.Property(a => a.FileName).HasMaxLength(260);
        b.Property(a => a.ContentType).HasMaxLength(100).IsUnicode(false);
        b.Property(a => a.UploadedAt).ValueGeneratedOnAdd();
        b.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}
