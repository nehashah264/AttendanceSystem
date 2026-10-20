using AttendanceSystem.Api.Services;
using AttendanceSystem.Api.Validation;
using FluentValidation;

namespace AttendanceSystem.Api;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAttendanceApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AttendanceOptions>()
            .Bind(configuration.GetSection(AttendanceOptions.SectionName))
            .Validate(
                options => options.ChronicAbsenceDays > 0,
                "Attendance:ChronicAbsenceDays must be greater than zero.")
            .ValidateOnStart();

        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IValidationHandler, ValidationHandler>();
        services.AddScoped<ISaveFaultInjector, NoOpSaveFaultInjector>();
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<SaveDailyAttendanceRequestValidator>();
        return services;
    }
}
