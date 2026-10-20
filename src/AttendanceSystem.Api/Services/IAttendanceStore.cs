using AttendanceSystem.Api.Domain;
using AttendanceSystem.Api.Dtos;

namespace AttendanceSystem.Api.Services;

public interface IAttendanceStore
{
    Task<School?> FindSchoolAsync(int schoolId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Student>> FindStudentsByIdsAsync(IReadOnlyCollection<int> studentIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, AttendanceCode>> FindCodesByValueAsync(IReadOnlyCollection<string> codeValues, CancellationToken cancellationToken);

    Task<int?> FindTermIdAsync(int schoolId, DateOnly attendDate, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, StudentAttendance>> FindAttendanceOnDateAsync(DateOnly attendDate, IReadOnlyCollection<int> studentIds, CancellationToken cancellationToken);

    Task AddAttendanceAsync(IReadOnlyList<StudentAttendance> rows, CancellationToken cancellationToken);

    Task<int> CountAbsencesAsync(int studentId, int schoolId, DateOnly yearStart, DateOnly yearEnd, CancellationToken cancellationToken);

    Task UpsertSummaryAsync(int studentId, int schoolId, string schoolYear, int totalAbsences, CancellationToken cancellationToken);

    Task<bool> HasOpenChronicAlertAsync(int studentId, string schoolYear, CancellationToken cancellationToken);

    Task AddChronicAlertAsync(int studentId, int schoolId, string schoolYear, int totalAbsences, CancellationToken cancellationToken);

    Task AddSubmissionLogAsync(int schoolId, DateOnly attendDate, int recordCount, string submittedBy, CancellationToken cancellationToken);

    Task<Student?> FindStudentAsync(int studentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttendanceHistoryItem>> GetHistoryAsync(int studentId, DateOnly yearStart, DateOnly yearEnd, CancellationToken cancellationToken);

    Task<ChronicSnapshot?> GetChronicSnapshotAsync(int studentId, string schoolYear, CancellationToken cancellationToken);
}

public sealed class ChronicSnapshot
{
    public int TotalAbsences { get; init; }

    public int? ConfiguredThreshold { get; init; }
}
