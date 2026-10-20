using AttendanceSystem.Api.Domain.Exceptions;
using AttendanceSystem.Api.Services;
using FluentValidation;

namespace AttendanceSystem.Api.Validation;

public sealed class ValidationHandler : IValidationHandler
{
    private readonly IServiceProvider _services;

    public ValidationHandler(IServiceProvider services)
    {
        _services = services;
    }

    public async Task ValidateAndThrowAsync<T>(T instance, CancellationToken cancellationToken = default)
    {
        if (instance is null)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["request"] = ["A request body is required."]
            });
        }

        var validator = _services.GetService<IValidator<T>>();
        if (validator is null)
        {
            return;
        }

        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
        throw new ValidationFailedException(errors);
    }
}
