using Erp.Security.Application;
using Erp.Security.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Security.Infrastructure.Persistence;

/// <summary>
/// دیتابیس مرکزی (schema sec). ساختار فقط با اسکریپت‌های پوشه database/central.
/// اتصال جدا از دیتابیس شرکت است و در تراکنش شرکت شرکت نمی‌کند.
/// </summary>
public class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options), ISecurityDbContext
{
    public IQueryable<User> Users => Set<User>();
    public IQueryable<UserCompany> UserCompanies => Set<UserCompany>();
    public IQueryable<Role> Roles => Set<Role>();
    public IQueryable<Company> Companies => Set<Company>();
    public IQueryable<SecurityLogEntry> SecurityLog => Set<SecurityLogEntry>();
    public IQueryable<SecuritySetting> Settings => Set<SecuritySetting>();

    void ISecurityDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void ISecurityDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> ISecurityDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.HasDefaultSchema("sec");

        m.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserName).HasMaxLength(50);
            b.Property(u => u.DisplayName).HasMaxLength(150);
            b.Property(u => u.PasswordHash).HasMaxLength(256).IsUnicode(false);
            b.Property(u => u.LockoutUntil).HasColumnType("datetime2(0)");
            b.Property(u => u.LastLoginAt).HasColumnType("datetime2(0)");
            b.Property(u => u.PasswordChangedAt).HasColumnType("datetime2(0)");
            b.Property(u => u.CreatedAt).HasColumnType("datetime2(0)");
            b.HasMany(u => u.Companies).WithOne().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Companies).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Ignore(u => u.DomainEvents);
        });

        m.Entity<UserCompany>(b =>
        {
            b.ToTable("UserCompanies");
            b.HasKey(c => new { c.UserId, c.CompanyId });
            b.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(c => c.Roles).WithOne().HasForeignKey(r => new { r.UserId, r.CompanyId }).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(c => c.Scopes).WithOne().HasForeignKey(s => new { s.UserId, s.CompanyId }).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(c => c.Scopes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        m.Entity<UserCompanyRole>(b =>
        {
            b.ToTable("UserCompanyRoles");
            b.HasKey(r => new { r.UserId, r.CompanyId, r.RoleId });
            b.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<DataScope>(b =>
        {
            b.ToTable("DataScopes");
            b.HasKey(s => new { s.UserId, s.CompanyId, s.Kind, s.ItemId });
        });

        m.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Title).HasMaxLength(100);
            b.Property(r => r.Description).HasMaxLength(300);
            b.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Ignore(r => r.DomainEvents);
        });

        m.Entity<RolePermission>(b =>
        {
            b.ToTable("RolePermissions");
            b.HasKey(p => new { p.RoleId, p.PermissionKey });
            b.Property(p => p.PermissionKey).HasMaxLength(80).IsUnicode(false);
        });

        m.Entity<Company>(b =>
        {
            b.ToTable("Companies");
            b.HasKey(c => c.Id);
            b.Property(c => c.Key).HasColumnName("Key").HasMaxLength(50).IsUnicode(false);
            b.Property(c => c.Title).HasMaxLength(150);
            b.Property(c => c.ConnectionString).HasMaxLength(500);
            b.Property(c => c.CreatedAt).HasColumnType("datetime2(0)");
        });

        m.Entity<SecurityLogEntry>(b =>
        {
            b.ToTable("SecurityLog", t => t.HasTrigger("TR_SecurityLog_ReadOnly"));
            b.HasKey(l => l.Id);
            b.Property(l => l.EventType).HasConversion<byte>();
            b.Property(l => l.UserName).HasMaxLength(50);
            b.Property(l => l.CompanyKey).HasMaxLength(50).IsUnicode(false);
            b.Property(l => l.Ip).HasMaxLength(45).IsUnicode(false);
            b.Property(l => l.Details).HasMaxLength(1000);
            b.Property(l => l.At).HasColumnType("datetime2(3)");
        });

        m.Entity<SecuritySetting>(b =>
        {
            b.ToTable("Settings");
            b.HasKey(s => s.Key);
            b.Property(s => s.Key).HasMaxLength(50).IsUnicode(false);
            b.Property(s => s.Title).HasMaxLength(150);
        });
    }
}
