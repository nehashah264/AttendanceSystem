using AttendanceSystem.Api.Domain;
using AttendanceSystem.Api.Domain.Exceptions;
using AttendanceSystem.Api.Dtos;
using Microsoft.Extensions.Options;

namespace AttendanceSystem.Api.Services;

public sealed class AttendanceService : IAttendanceService
{
    private const string LegacyUser = "SYSTEM_USER";
    private readonly IValidationHandler _validation;
    private readonly IAttendanceStore _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISaveFaultInjector _faultInjector;
    private readonly TimeProvider _time;
    private readonly AttendanceOptions _options;

    public AttendanceService(
        IValidationHandler validation,
        IAttendanceStore store,
        IUnitOfWork unitOfWork,
        ISaveFaultInjector faultInjector,
        TimeProvider time,
        IOptions<AttendanceOptions> options)
    {
        _validation = validation;
        _store = store;
        _unitOfWork = unitOfWork;
        _faultInjector = faultInjector;
        _time = time;
        _options = options.Value;
    }

    public async Task<SaveDailyAttendanceResponse> SaveDailyAsync(
        SaveDailyAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        await _validation.ValidateAndThrowAsync(request, cancellationToken);

        SaveDailyAttendanceResponse? response = null;
        await _unitOfWork.ExecuteAsync(async ct =>
        {
            var school = await _store.FindSchoolAsync(request.SchoolId, ct);
            if (school is null)
            {
                throw new NotFoundException($"School {request.SchoolId} was not found.");
            }

            foreach (var entry in request.Students)
            {
                entry.AttendCode = string.IsNullOrWhiteSpace(entry.AttendCode) ? "P" : entry.AttendCode.Trim();
            }

            var studentIds = request.Students.Select(student => student.StudentId).Distinct().ToArray();
            var students = await _store.FindStudentsByIdsAsync(studentIds, ct);
            var studentsById = students.ToDictionary(student => student.StudentId);
            var missing = studentIds
                .Where(id => !studentsById.ContainsKey(id))
                .OrderBy(id => id)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new NotFoundException(
                    $"Students not found: {string.Join(", ", missing)}.");
            }

            var codes = await _store.FindCodesByValueAsync(
                request.Students.Select(student => student.AttendCode).Distinct(StringComparer.Ordinal).ToArray(),
                ct);
            var codeErrors = new Dictionary<string, string[]>();
            for (var index = 0; index < request.Students.Count; index++)
            {
                var code = request.Students[index].AttendCode;
                if (!codes.ContainsKey(code))
                {
                    codeErrors[$"Students[{index}].AttendCode"] = [$"Attendance code '{code}' is not defined."];
                }
            }

            if (codeErrors.Count > 0)
            {
                throw new ValidationFailedException(codeErrors);
            }

            var existing = await _store.FindAttendanceOnDateAsync(request.AttendDate, studentIds, ct);
            var termId = await _store.FindTermIdAsync(request.SchoolId, request.AttendDate, ct);
            var savedAt = _time.GetUtcNow().UtcDateTime;
            var schoolYear = SchoolYearCalculator.GetSchoolYear(request.AttendDate);
            var (yearStart, yearEnd) = SchoolYearCalculator.GetRange(schoolYear);
            var threshold = ChronicAbsenteeismCalculator.ResolveThreshold(
                school.AbsenceAlertThreshold,
                _options.ChronicAbsenceDays);

            var pending = new Dictionary<int, StudentAttendance>(existing);
            var inserts = new List<StudentAttendance>();
            var saved = new List<SavedAttendanceItem>();
            var savedIndex = new Dictionary<int, int>();
            foreach (var entry in request.Students)
            {
                var code = codes[entry.AttendCode];
                var notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim();
                if (pending.TryGetValue(entry.StudentId, out var current))
                {
                    current.AttendCode = code.CodeValue;
                    current.IsAbsent = code.IsAbsent;
                    current.IsExcused = code.IsExcused;
                    current.MinutesLate = entry.MinutesLate;
                    current.Notes = notes;
                    current.ModifiedDate = savedAt;
                    current.ModifiedBy = LegacyUser;
                    var operation = savedIndex.TryGetValue(entry.StudentId, out var index)
                        ? saved[index].Operation
                        : "Updated";
                    Remember(current, operation);
                    continue;
                }

                var created = new StudentAttendance
                {
                    StudentId = entry.StudentId,
                    SchoolId = request.SchoolId,
                    AttendDate = request.AttendDate,
                    TermId = termId,
                    AttendCode = code.CodeValue,
                    IsAbsent = code.IsAbsent,
                    IsExcused = code.IsExcused,
                    MinutesLate = entry.MinutesLate,
                    Notes = notes,
                    CreatedDate = savedAt,
                    CreatedBy = LegacyUser
                };
                inserts.Add(created);
                pending[entry.StudentId] = created;
                Remember(created, "Inserted");
            }

            await _store.AddAttendanceAsync(inserts, ct);

            foreach (var row in saved)
            {
                var total = await _store.CountAbsencesAsync(row.StudentId, request.SchoolId, yearStart, yearEnd, ct);
                await _store.UpsertSummaryAsync(row.StudentId, request.SchoolId, schoolYear, total, ct);
                if (!ChronicAbsenteeismCalculator.IsChronicallyAbsent(total, threshold))
                {
                    continue;
                }

                var hasOpenAlert = await _store.HasOpenChronicAlertAsync(row.StudentId, schoolYear, ct);
                if (!hasOpenAlert)
                {
                    await _store.AddChronicAlertAsync(row.StudentId, request.SchoolId, schoolYear, total, ct);
                }
            }

            await _store.AddSubmissionLogAsync(request.SchoolId, request.AttendDate, request.Students.Count, LegacyUser, ct);
            _faultInjector.ThrowIfRequested();

            response = new SaveDailyAttendanceResponse
            {
                SchoolId = request.SchoolId,
                AttendDate = request.AttendDate,
                SchoolYear = schoolYear,
                TermId = termId,
                RecordCount = saved.Count,
                Students = saved
            };

            void Remember(StudentAttendance row, string operation)
            {
                var item = ToSavedItem(row, operation);
                if (savedIndex.TryGetValue(row.StudentId, out var index))
                {
                    saved[index] = item;
                    return;
                }

                savedIndex[row.StudentId] = saved.Count;
                saved.Add(item);
            }
        }, cancellationToken);

        return response ?? throw new InvalidOperationException("The attendance batch did not produce a response.");
    }

    private static SavedAttendanceItem ToSavedItem(StudentAttendance row, string operation) => new()
    {
        StudentId = row.StudentId,
        AttendCode = row.AttendCode,
        IsAbsent = row.IsAbsent,
        IsExcused = row.IsExcused,
        MinutesLate = row.MinutesLate ?? 0,
        Notes = row.Notes,
        Operation = operation
    };

    public async Task<AttendanceHistoryResponse> GetHistoryAsync(
        int studentId,
        string? schoolYear,
        CancellationToken cancellationToken)
    {
        var query = new StudentYearQuery { StudentId = studentId, SchoolYear = schoolYear };
        await _validation.ValidateAndThrowAsync(query, cancellationToken);

        var year = ResolveSchoolYear(schoolYear);
        var (start, end) = SchoolYearCalculator.GetRange(year);
        var records = await _store.GetHistoryAsync(studentId, start, end, cancellationToken);
        return new AttendanceHistoryResponse
        {
            StudentId = studentId,
            SchoolYear = year,
            Records = records
        };
    }

    public async Task<ChronicAbsenteeismResponse> GetChronicAbsenteeismAsync(
        int studentId,
        string? schoolYear,
        CancellationToken cancellationToken)
    {
        var query = new StudentYearQuery { StudentId = studentId, SchoolYear = schoolYear };
        await _validation.ValidateAndThrowAsync(query, cancellationToken);

        var year = ResolveSchoolYear(schoolYear);
        var snapshot = await _store.GetChronicSnapshotAsync(studentId, year, cancellationToken);
        if (snapshot is null)
        {
            throw new NotFoundException($"No attendance summary exists for student {studentId} in {year}.");
        }

        var threshold = ChronicAbsenteeismCalculator.ResolveThreshold(
            snapshot.ConfiguredThreshold,
            _options.ChronicAbsenceDays);
        return new ChronicAbsenteeismResponse
        {
            StudentId = studentId,
            SchoolYear = year,
            TotalAbsences = snapshot.TotalAbsences,
            AbsenceAlertThreshold = threshold,
            IsChronicallyAbsent = ChronicAbsenteeismCalculator.IsChronicallyAbsent(snapshot.TotalAbsences, threshold)
        };
    }

    private string ResolveSchoolYear(string? schoolYear)
    {
        if (!string.IsNullOrWhiteSpace(schoolYear))
        {
            return schoolYear.Trim();
        }

        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        return SchoolYearCalculator.GetSchoolYear(today);
    }
}
