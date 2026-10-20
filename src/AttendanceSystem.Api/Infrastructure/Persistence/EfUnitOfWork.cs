using AttendanceSystem.Api.Domain.Exceptions;
using AttendanceSystem.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AttendanceDbContext _db;

    public EfUnitOfWork(AttendanceDbContext db)
    {
        _db = db;
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException(
                "Attendance already exists for one or more students on this date.",
                Array.Empty<int>());
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsUniqueConstraint(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
