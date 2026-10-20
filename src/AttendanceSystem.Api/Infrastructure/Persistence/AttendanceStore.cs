using AttendanceSystem.Api.Domain;
using AttendanceSystem.Api.Dtos;
using AttendanceSystem.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Infrastructure.Persistence;

public sealed class AttendanceStore : IAttendanceStore
{
    private readonly AttendanceDbContext _db;

    public AttendanceStore(AttendanceDbContext db)
    {
        _db = db;
    }

    public Task<School?> FindSchoolAsync(int schoolId, CancellationToken cancellationToken) =>
        _db.Schools.FirstOrDefaultAsync(school => school.SchoolId == schoolId, cancellationToken);

    public async Task<IReadOnlyList<Student>> FindStudentsByIdsAsync(
        IReadOnlyCollection<int> studentIds,
        CancellationToken cancellationToken)
    {
        return await _db.Students
            .Where(student => studentIds.Contains(student.StudentId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, AttendanceCode>> FindCodesByValueAsync(
        IReadOnlyCollection<string> codeValues,
        CancellationToken cancellationToken)
    {
        var codes = await _db.AttendanceCodes
            .Where(code => codeValues.Contains(code.CodeValue))
            .ToListAsync(cancellationToken);
        return codes.ToDictionary(code => code.CodeValue, StringComparer.Ordinal);
    }

    public Task<int?> FindTermIdAsync(int schoolId, DateOnly attendDate, CancellationToken cancellationToken) =>
        _db.SchoolTerms
            .Where(term => term.SchoolId == schoolId && term.StartDate <= attendDate && term.EndDate >= attendDate)
            .OrderBy(term => term.TermId)
            .Select(term => (int?)term.TermId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, StudentAttendance>> FindAttendanceOnDateAsync(
        DateOnly attendDate,
        IReadOnlyCollection<int> studentIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.StudentAttendances
            .Where(row => row.AttendDate == attendDate && studentIds.Contains(row.StudentId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.StudentId);
    }

    public async Task AddAttendanceAsync(IReadOnlyList<StudentAttendance> rows, CancellationToken cancellationToken)
    {
        _db.StudentAttendances.AddRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAbsencesAsync(
        int studentId,
        int schoolId,
        DateOnly yearStart,
        DateOnly yearEnd,
        CancellationToken cancellationToken) =>
        _db.StudentAttendances.CountAsync(
            row => row.StudentId == studentId
                && row.SchoolId == schoolId
                && row.AttendDate >= yearStart
                && row.AttendDate <= yearEnd
                && row.IsAbsent,
            cancellationToken);

    public async Task UpsertSummaryAsync(
        int studentId,
        int schoolId,
        string schoolYear,
        int totalAbsences,
        CancellationToken cancellationToken)
    {
        var summary = await _db.StudentAttendanceSummaries
            .FirstOrDefaultAsync(
                row => row.StudentId == studentId && row.SchoolYear == schoolYear,
                cancellationToken);
        if (summary is null)
        {
            _db.StudentAttendanceSummaries.Add(new StudentAttendanceSummary
            {
                StudentId = studentId,
                SchoolId = schoolId,
                SchoolYear = schoolYear,
                TotalAbsences = totalAbsences,
                LastUpdated = DateTime.UtcNow
            });
        }
        else
        {
            summary.TotalAbsences = totalAbsences;
            summary.LastUpdated = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasOpenChronicAlertAsync(int studentId, string schoolYear, CancellationToken cancellationToken) =>
        _db.StudentAlerts.AnyAsync(
            alert => alert.StudentId == studentId
                && alert.SchoolYear == schoolYear
                && alert.AlertType == AlertTypes.ChronicAbsence
                && alert.ResolvedDate == null,
            cancellationToken);

    public async Task AddChronicAlertAsync(
        int studentId,
        int schoolId,
        string schoolYear,
        int totalAbsences,
        CancellationToken cancellationToken)
    {
        _db.StudentAlerts.Add(new StudentAlert
        {
            StudentId = studentId,
            SchoolId = schoolId,
            AlertType = AlertTypes.ChronicAbsence,
            SchoolYear = schoolYear,
            AlertDate = DateTime.UtcNow,
            AlertMessage = $"Student has reached {totalAbsences} absences this year"
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddSubmissionLogAsync(
        int schoolId,
        DateOnly attendDate,
        int recordCount,
        string submittedBy,
        CancellationToken cancellationToken)
    {
        _db.AttendanceSubmissionLogs.Add(new AttendanceSubmissionLog
        {
            SchoolId = schoolId,
            SubmittedDate = DateTime.UtcNow,
            AttendDate = attendDate,
            RecordCount = recordCount,
            SubmittedBy = submittedBy
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<Student?> FindStudentAsync(int studentId, CancellationToken cancellationToken) =>
        _db.Students.FirstOrDefaultAsync(student => student.StudentId == studentId, cancellationToken);

    public async Task<IReadOnlyList<AttendanceHistoryItem>> GetHistoryAsync(
        int studentId,
        DateOnly yearStart,
        DateOnly yearEnd,
        CancellationToken cancellationToken)
    {
        return await (
            from attendance in _db.StudentAttendances
            join code in _db.AttendanceCodes on attendance.AttendCode equals code.CodeValue
            join term in _db.SchoolTerms on attendance.TermId equals term.TermId into terms
            from term in terms.DefaultIfEmpty()
            where attendance.StudentId == studentId
                && attendance.AttendDate >= yearStart
                && attendance.AttendDate <= yearEnd
            orderby attendance.AttendDate descending
            select new AttendanceHistoryItem
            {
                AttendDate = attendance.AttendDate,
                AttendCode = attendance.AttendCode,
                Description = code.Description,
                IsAbsent = attendance.IsAbsent,
                IsExcused = attendance.IsExcused,
                MinutesLate = attendance.MinutesLate,
                Notes = attendance.Notes,
                TermName = term != null ? term.TermName : null
            }).ToListAsync(cancellationToken);
    }

    public async Task<ChronicSnapshot?> GetChronicSnapshotAsync(
        int studentId,
        string schoolYear,
        CancellationToken cancellationToken)
    {
        var summary = await _db.StudentAttendanceSummaries
            .FirstOrDefaultAsync(
                row => row.StudentId == studentId && row.SchoolYear == schoolYear,
                cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var threshold = await _db.Schools
            .Where(school => school.SchoolId == summary.SchoolId)
            .Select(school => school.AbsenceAlertThreshold)
            .FirstOrDefaultAsync(cancellationToken);
        return new ChronicSnapshot
        {
            TotalAbsences = summary.TotalAbsences,
            ConfiguredThreshold = threshold
        };
    }
}
