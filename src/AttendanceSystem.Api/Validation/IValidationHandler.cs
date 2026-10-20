namespace AttendanceSystem.Api.Services;

public interface IValidationHandler
{
    Task ValidateAndThrowAsync<T>(T instance, CancellationToken cancellationToken = default);
}
