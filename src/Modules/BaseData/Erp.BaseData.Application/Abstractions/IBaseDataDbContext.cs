using Erp.BaseData.Domain.Banks;
using Erp.BaseData.Domain.Lookups;
using Erp.BaseData.Domain.Persons;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Application.Abstractions;

public interface IBaseDataDbContext
{
    DbSet<Person> Persons { get; }
    DbSet<PersonRoleType> PersonRoleTypes { get; }
    DbSet<PersonAttachment> PersonAttachments { get; }
    DbSet<LookupCategory> LookupCategories { get; }
    DbSet<LookupItem> LookupItems { get; }
    DbSet<Bank> Banks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

