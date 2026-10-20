using AttendanceSystem.Api.Dtos;

namespace AttendanceSystem.Api.Services;

public interface IAttendanceService
{
    Task<SaveDailyAttendanceResponse> SaveDailyAsync(SaveDailyAttendanceRequest request, CancellationToken cancellationToken);

    Task<AttendanceHistoryResponse> GetHistoryAsync(int studentId, string? schoolYear, CancellationToken cancellationToken);

    Task<ChronicAbsenteeismResponse> GetChronicAbsenteeismAsync(int studentId, string? schoolYear, CancellationToken cancellationToken);
}
