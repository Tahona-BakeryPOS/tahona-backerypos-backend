using System.Linq.Expressions;
using BakeryPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Infrastructure.Persistence.Repositories;

public class GenericRepository<T> : IRepository<T> where T : AuditableEntity
{
    protected readonly ApplicationDbContext Context;
    protected readonly DbSet<T> DbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet.FindAsync([id], ct);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
    {
        var orgProperty = typeof(T).GetProperty("OrganizationId");
        if (orgProperty is null)
            return await DbSet.ToListAsync(ct);

        var parameter = Expression.Parameter(typeof(T), "e");
        var property = Expression.Property(parameter, orgProperty);
        var constant = Expression.Constant(organizationId);
        var equal = Expression.Equal(property, constant);
        var lambda = Expression.Lambda<Func<T, bool>>(equal, parameter);

        return await DbSet.Where(lambda).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return await DbSet.Where(predicate).ToListAsync(ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return await DbSet.FirstOrDefaultAsync(predicate, ct);
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(predicate, ct);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        return predicate is null
            ? await DbSet.CountAsync(ct)
            : await DbSet.CountAsync(predicate, ct);
    }

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await DbSet.AddAsync(entity, ct);
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        await DbSet.AddRangeAsync(entities, ct);
    }

    public void Update(T entity)
    {
        DbSet.Update(entity);
    }

    public void SoftDelete(T entity, string deletedBy)
    {
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = deletedBy;
        DbSet.Update(entity);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet.AnyAsync(e => e.Id == id, ct);
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
