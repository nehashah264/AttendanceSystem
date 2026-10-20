namespace AttendanceSystem.Api.Services;

public interface IUnitOfWork
{
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
