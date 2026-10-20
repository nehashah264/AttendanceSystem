using AttendanceSystem.Api.Infrastructure.Persistence;
using AttendanceSystem.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAttendanceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Attendance") ?? "Data Source=attendance.db";
        services.AddDbContext<AttendanceDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAttendanceStore, AttendanceStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
