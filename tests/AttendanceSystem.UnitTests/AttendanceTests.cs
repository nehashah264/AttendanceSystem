using AttendanceSystem.Api.Domain;
using AttendanceSystem.Api.Domain.Exceptions;
using AttendanceSystem.Api.Dtos;
using AttendanceSystem.Api.Services;
using AttendanceSystem.Api.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AttendanceSystem.UnitTests;

public sealed class SchoolYearCalculatorTests
{
    [Theory]
    [InlineData(2026, 9, 1, "2026-2027")]
    [InlineData(2026, 10, 20, "2026-2027")]
    [InlineData(2026, 8, 31, "2025-2026")]
    public void GetSchoolYear_UsesSeptemberBoundary(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, SchoolYearCalculator.GetSchoolYear(new DateOnly(year, month, day)));
    }

    [Fact]
    public void GetRange_CoversSeptemberThroughAugust()
    {
        var current = SchoolYearCalculator.GetRange("2026-2027");
        Assert.Equal(new DateOnly(2026, 9, 1), current.Start);
        Assert.Equal(new DateOnly(2027, 8, 31), current.End);

        var previous = SchoolYearCalculator.GetRange(new DateOnly(2026, 8, 31));
        Assert.Equal(new DateOnly(2025, 9, 1), previous.Start);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.End);
    }
}

public sealed class ChronicAbsenteeismCalculatorTests
{
    [Theory]
    [InlineData(9, 10, false)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(4, 5, false)]
    [InlineData(5, 5, true)]
    public void IsChronicallyAbsent_ComparesTotalWithThreshold(int total, int threshold, bool expected)
    {
        Assert.Equal(expected, ChronicAbsenteeismCalculator.IsChronicallyAbsent(total, threshold));
    }

    [Fact]
    public void ResolveThreshold_UsesConfiguredDaysWhenSchoolThresholdIsNull()
    {
        Assert.Equal(10, ChronicAbsenteeismCalculator.ResolveThreshold(null, 10));
        Assert.Equal(8, ChronicAbsenteeismCalculator.ResolveThreshold(null, 8));
        Assert.Equal(5, ChronicAbsenteeismCalculator.ResolveThreshold(5, 10));
        Assert.False(ChronicAbsenteeismCalculator.IsChronicallyAbsent(9, ChronicAbsenteeismCalculator.ResolveThreshold(null, 10)));
        Assert.True(ChronicAbsenteeismCalculator.IsChronicallyAbsent(10, ChronicAbsenteeismCalculator.ResolveThreshold(null, 10)));
    }
}

public sealed class SaveDailyAttendanceRequestValidatorTests
{
    private readonly SaveDailyAttendanceRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_Passes()
    {
        var result = _validator.Validate(Request(Entry(1, "P", 0, null), Entry(2, "A", 5, "note")));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyBatch_Fails()
    {
        var result = _validator.Validate(Request());
        Assert.False(result.IsValid);
    }

    [Fact]
    public void DuplicateStudent_Passes()
    {
        var result = _validator.Validate(Request(Entry(1, "P", 0, null), Entry(1, "A", 0, null)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NegativeMinutes_Fails()
    {
        var result = _validator.Validate(Request(Entry(1, "P", -1, null)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void BlankCode_Passes()
    {
        var result = _validator.Validate(Request(Entry(1, "", 0, null)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NotesOver500_Fails()
    {
        var result = _validator.Validate(Request(Entry(1, "P", 0, new string('x', 501))));
        Assert.False(result.IsValid);
    }

    private static SaveDailyAttendanceRequest Request(params AttendanceEntryRequest[] students) => new()
    {
        SchoolId = 1,
        AttendDate = new DateOnly(2026, 10, 20),
        Students = students.ToList()
    };

    private static AttendanceEntryRequest Entry(int studentId, string code, int minutes, string? notes) => new()
    {
        StudentId = studentId,
        AttendCode = code,
        MinutesLate = minutes,
        Notes = notes
    };
}

public sealed class StudentYearQueryValidatorTests
{
    private readonly StudentYearQueryValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-2027")]
    public void AcceptsBlankOrConsecutiveYear(string? schoolYear)
    {
        var result = _validator.Validate(new StudentYearQuery { StudentId = 1, SchoolYear = schoolYear });
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026-27")]
    [InlineData("2026-2028")]
    [InlineData("abcd-efgh")]
    public void RejectsMalformedYear(string schoolYear)
    {
        var result = _validator.Validate(new StudentYearQuery { StudentId = 1, SchoolYear = schoolYear });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RejectsNonPositiveStudentId()
    {
        var result = _validator.Validate(new StudentYearQuery { StudentId = 0, SchoolYear = "2026-2027" });
        Assert.False(result.IsValid);
    }
}

public sealed class AttendanceServiceTests
{
    [Fact]
    public async Task Save_WhenSchoolMissing_ThrowsNotFound()
    {
        var store = StoreWithCodes();
        var service = CreateService(store);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SaveDailyAsync(Request(1, Entry(1, "P")), CancellationToken.None));
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task Save_WhenSchoolInactive_StillSaves()
    {
        var store = StoreWithCodes();
        store.Schools.Add(new School { SchoolId = 3, Active = false, AbsenceAlertThreshold = 10 });
        store.Students.Add(new Student { StudentId = 7, SchoolId = 3, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(3, Entry(7, "P")), CancellationToken.None);

        Assert.Equal("Inserted", response.Students.Single().Operation);
        Assert.Equal("SYSTEM_USER", store.Added.Single().CreatedBy);
        Assert.Equal("SYSTEM_USER", store.Logs.Single().By);
    }

    [Fact]
    public async Task Save_WhenStudentMissing_ThrowsNotFound()
    {
        var store = StoreWithCodes();
        store.Schools.Add(ActiveSchool(1, 10));
        var service = CreateService(store);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SaveDailyAsync(Request(1, Entry(99, "P")), CancellationToken.None));
    }

    [Fact]
    public async Task Save_WhenStudentInactiveOrAtAnotherSchool_StillSaves()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 4, SchoolId = 1, Active = false });
        store.Students.Add(new Student { StudentId = 5, SchoolId = 2, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(4, "A"), Entry(5, "P")), CancellationToken.None);

        Assert.Equal(2, response.Students.Count);
        Assert.Equal(2, store.Added.Count);
        Assert.Equal(1, store.Added.Single(row => row.StudentId == 5).SchoolId);
    }

    [Fact]
    public async Task Save_WhenDayAlreadyExists_UpdatesTheRow()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        store.Students.Add(new Student { StudentId = 2, SchoolId = 1, Active = true });
        store.Existing.Add(new StudentAttendance
        {
            StudentId = 1,
            SchoolId = 1,
            AttendDate = new DateOnly(2026, 10, 22),
            AttendCode = "P",
            IsAbsent = false
        });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(1, "A"), Entry(2, "P")), CancellationToken.None);

        Assert.Equal("Updated", response.Students.Single(item => item.StudentId == 1).Operation);
        Assert.Equal("Inserted", response.Students.Single(item => item.StudentId == 2).Operation);
        Assert.Equal("A", store.Existing.Single(row => row.StudentId == 1).AttendCode);
        Assert.True(store.Existing.Single(row => row.StudentId == 1).IsAbsent);
        Assert.Equal("SYSTEM_USER", store.Existing.Single(row => row.StudentId == 1).ModifiedBy);
        Assert.Single(store.Added);
        Assert.Equal(1, store.Summaries.Single(row => row.StudentId == 1).Total);
        Assert.Equal(0, store.Summaries.Single(row => row.StudentId == 2).Total);
    }

    [Fact]
    public async Task Save_WhenDayAlreadyExists_LeavesSchoolAndTerm()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 2, Active = true });
        store.Existing.Add(new StudentAttendance
        {
            StudentId = 1,
            SchoolId = 2,
            TermId = 8,
            AttendDate = new DateOnly(2026, 10, 22),
            AttendCode = "P",
            IsAbsent = false
        });
        var service = CreateService(store);

        await service.SaveDailyAsync(Request(1, Entry(1, "A")), CancellationToken.None);

        var row = store.Existing.Single();
        Assert.Equal(2, row.SchoolId);
        Assert.Equal(8, row.TermId);
        Assert.Equal("A", row.AttendCode);
        Assert.Equal(0, store.Summaries.Single().Total);
    }

    [Fact]
    public async Task Save_WhenStudentRepeated_KeepsTheLastCode()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(1, "P"), Entry(1, "A")), CancellationToken.None);

        Assert.Single(response.Students);
        Assert.Equal("A", response.Students.Single().AttendCode);
        Assert.Equal("Inserted", response.Students.Single().Operation);
        Assert.Equal("A", store.Added.Single().AttendCode);
        Assert.Equal(2, store.Logs.Single().Count);
    }

    [Fact]
    public async Task Save_WhenCodeBlank_StoresPresent()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(1, " ")), CancellationToken.None);

        Assert.Equal("P", response.Students.Single().AttendCode);
        Assert.False(store.Added.Single().IsAbsent);
    }

    [Fact]
    public async Task Save_WhenCodeInactive_UsesItsFlags()
    {
        var store = ReadyStore();
        store.Codes["X"] = new AttendanceCode { CodeValue = "X", IsAbsent = true, IsExcused = false, IsActive = false };
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(1, "X")), CancellationToken.None);

        Assert.True(response.Students.Single().IsAbsent);
        Assert.Equal("X", store.Added.Single().AttendCode);
    }

    [Fact]
    public async Task Save_WhenCodeUnknown_ThrowsValidation()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        var service = CreateService(store);
        await Assert.ThrowsAsync<ValidationFailedException>(() => service.SaveDailyAsync(Request(1, Entry(1, "Z")), CancellationToken.None));
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task Save_CopiesCodeFlags_AndRecountsAbsences()
    {
        var store = ReadyStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        store.Students.Add(new Student { StudentId = 2, SchoolId = 1, Active = true });
        var service = CreateService(store);

        var response = await service.SaveDailyAsync(Request(1, Entry(1, "P"), Entry(2, "E")), CancellationToken.None);

        Assert.Equal(2, response.RecordCount);
        Assert.False(store.Added.Single(row => row.StudentId == 1).IsAbsent);
        var excused = store.Added.Single(row => row.StudentId == 2);
        Assert.True(excused.IsAbsent);
        Assert.True(excused.IsExcused);
        Assert.Equal(0, store.Summaries.Single(row => row.StudentId == 1).Total);
        Assert.Equal(1, store.Summaries.Single(row => row.StudentId == 2).Total);
        Assert.Empty(store.AddedAlerts);
        Assert.Single(store.Logs);
    }

    [Fact]
    public async Task Save_InsertsChronicAlertOnce()
    {
        var store = ReadyStore();
        store.Schools[0].AbsenceAlertThreshold = null;
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        for (var day = 1; day <= 9; day++)
        {
            store.Existing.Add(new StudentAttendance
            {
                StudentId = 1,
                SchoolId = 1,
                AttendDate = new DateOnly(2026, 9, day),
                IsAbsent = true
            });
        }

        var service = CreateService(store, options: new AttendanceOptions { ChronicAbsenceDays = 10 });
        await service.SaveDailyAsync(Request(1, new DateOnly(2026, 10, 22), Entry(1, "A")), CancellationToken.None);
        Assert.Single(store.AddedAlerts);

        await service.SaveDailyAsync(Request(1, new DateOnly(2026, 10, 23), Entry(1, "A")), CancellationToken.None);
        Assert.Single(store.AddedAlerts);
    }

    [Fact]
    public async Task Save_WhenEmpty_ThrowsBeforeTheStoreIsUsed()
    {
        var store = new FakeAttendanceStore();
        var service = CreateService(store);
        await Assert.ThrowsAsync<ValidationFailedException>(() => service.SaveDailyAsync(Request(1), CancellationToken.None));
        Assert.Equal(0, store.SchoolLookups);
    }

    [Fact]
    public async Task GetHistory_WhenStudentMissing_ReturnsEmpty()
    {
        var service = CreateService(new FakeAttendanceStore());
        var history = await service.GetHistoryAsync(4, "2026-2027", CancellationToken.None);
        Assert.Empty(history.Records);
    }

    [Fact]
    public async Task GetHistory_ReturnsStoreRowsForTheResolvedYear()
    {
        var store = new FakeAttendanceStore();
        store.Students.Add(new Student { StudentId = 1, SchoolId = 1, Active = true });
        store.History.Add(new AttendanceHistoryItem { AttendDate = new DateOnly(2026, 10, 2), AttendCode = "P", Description = "Present" });
        var service = CreateService(store);

        var history = await service.GetHistoryAsync(1, null, CancellationToken.None);
        Assert.Equal("2026-2027", history.SchoolYear);
        Assert.Single(history.Records);
    }

    [Fact]
    public async Task GetChronic_UsesConfiguredDaysWhenSchoolThresholdIsNull()
    {
        var store = new FakeAttendanceStore();
        store.Snapshots[1] = new ChronicSnapshot { TotalAbsences = 8, ConfiguredThreshold = null };
        var service = CreateService(store, options: new AttendanceOptions { ChronicAbsenceDays = 8 });

        var status = await service.GetChronicAbsenteeismAsync(1, "2026-2027", CancellationToken.None);
        Assert.Equal(8, status.AbsenceAlertThreshold);
        Assert.True(status.IsChronicallyAbsent);
    }

    private static AttendanceService CreateService(
        FakeAttendanceStore store,
        FakeUnitOfWork? unitOfWork = null,
        AttendanceOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssemblyContaining<SaveDailyAttendanceRequestValidator>();
        var provider = services.BuildServiceProvider();
        return new AttendanceService(
            new ValidationHandler(provider),
            store,
            unitOfWork ?? new FakeUnitOfWork(),
            new NoOpSaveFaultInjector(),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)),
            Options.Create(options ?? new AttendanceOptions { ChronicAbsenceDays = 10 }));
    }

    private static FakeAttendanceStore StoreWithCodes()
    {
        var store = new FakeAttendanceStore();
        store.Codes["P"] = new AttendanceCode { CodeValue = "P", IsAbsent = false, IsExcused = false, IsActive = true };
        store.Codes["A"] = new AttendanceCode { CodeValue = "A", IsAbsent = true, IsExcused = false, IsActive = true };
        store.Codes["E"] = new AttendanceCode { CodeValue = "E", IsAbsent = true, IsExcused = true, IsActive = true };
        return store;
    }

    private static FakeAttendanceStore ReadyStore()
    {
        var store = StoreWithCodes();
        store.Schools.Add(ActiveSchool(1, 10));
        store.TermId = 3;
        return store;
    }

    private static School ActiveSchool(int id, int? threshold) => new()
    {
        SchoolId = id,
        Active = true,
        AbsenceAlertThreshold = threshold,
        SchoolName = "Lincoln"
    };

    private static SaveDailyAttendanceRequest Request(int schoolId, params AttendanceEntryRequest[] students) =>
        Request(schoolId, new DateOnly(2026, 10, 22), students);

    private static SaveDailyAttendanceRequest Request(int schoolId, DateOnly date, params AttendanceEntryRequest[] students) => new()
    {
        SchoolId = schoolId,
        AttendDate = date,
        SubmittedBy = "clerk",
        Students = students.ToList()
    };

    private static AttendanceEntryRequest Entry(int studentId, string code) => new()
    {
        StudentId = studentId,
        AttendCode = code,
        MinutesLate = 0
    };
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool Committed { get; private set; }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await action(cancellationToken);
        Committed = true;
    }
}

internal sealed class FakeAttendanceStore : IAttendanceStore
{
    public List<School> Schools { get; } = new();

    public List<Student> Students { get; } = new();

    public Dictionary<string, AttendanceCode> Codes { get; } = new(StringComparer.Ordinal);

    public int? TermId { get; set; }

    public List<StudentAttendance> Existing { get; } = new();

    public List<StudentAttendance> Added { get; } = new();

    public List<(int StudentId, string SchoolYear, int Total)> Summaries { get; } = new();

    public List<(int StudentId, string SchoolYear)> AddedAlerts { get; } = new();

    public List<(int SchoolId, DateOnly Date, int Count, string By)> Logs { get; } = new();

    public List<AttendanceHistoryItem> History { get; } = new();

    public Dictionary<int, ChronicSnapshot> Snapshots { get; } = new();

    public int SchoolLookups { get; private set; }

    public Task<School?> FindSchoolAsync(int schoolId, CancellationToken cancellationToken)
    {
        SchoolLookups++;
        return Task.FromResult(Schools.FirstOrDefault(school => school.SchoolId == schoolId));
    }

    public Task<IReadOnlyList<Student>> FindStudentsByIdsAsync(IReadOnlyCollection<int> studentIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Student>>(Students.Where(student => studentIds.Contains(student.StudentId)).ToList());

    public Task<IReadOnlyDictionary<string, AttendanceCode>> FindCodesByValueAsync(IReadOnlyCollection<string> codeValues, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, AttendanceCode>>(Codes.Where(pair => codeValues.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    public Task<int?> FindTermIdAsync(int schoolId, DateOnly attendDate, CancellationToken cancellationToken) =>
        Task.FromResult(TermId);

    public Task<IReadOnlyDictionary<int, StudentAttendance>> FindAttendanceOnDateAsync(DateOnly attendDate, IReadOnlyCollection<int> studentIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<int, StudentAttendance>>(Existing
            .Where(row => row.AttendDate == attendDate && studentIds.Contains(row.StudentId))
            .GroupBy(row => row.StudentId)
            .ToDictionary(group => group.Key, group => group.First()));

    public Task AddAttendanceAsync(IReadOnlyList<StudentAttendance> rows, CancellationToken cancellationToken)
    {
        Added.AddRange(rows);
        Existing.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<int> CountAbsencesAsync(int studentId, int schoolId, DateOnly yearStart, DateOnly yearEnd, CancellationToken cancellationToken) =>
        Task.FromResult(Existing.Count(row => row.StudentId == studentId && row.SchoolId == schoolId && row.AttendDate >= yearStart && row.AttendDate <= yearEnd && row.IsAbsent));

    public Task UpsertSummaryAsync(int studentId, int schoolId, string schoolYear, int totalAbsences, CancellationToken cancellationToken)
    {
        Summaries.RemoveAll(row => row.StudentId == studentId && row.SchoolYear == schoolYear);
        Summaries.Add((studentId, schoolYear, totalAbsences));
        return Task.CompletedTask;
    }

    public Task<bool> HasOpenChronicAlertAsync(int studentId, string schoolYear, CancellationToken cancellationToken) =>
        Task.FromResult(AddedAlerts.Any(alert => alert.StudentId == studentId && alert.SchoolYear == schoolYear));

    public Task AddChronicAlertAsync(int studentId, int schoolId, string schoolYear, int totalAbsences, CancellationToken cancellationToken)
    {
        AddedAlerts.Add((studentId, schoolYear));
        return Task.CompletedTask;
    }

    public Task AddSubmissionLogAsync(int schoolId, DateOnly attendDate, int recordCount, string submittedBy, CancellationToken cancellationToken)
    {
        Logs.Add((schoolId, attendDate, recordCount, submittedBy));
        return Task.CompletedTask;
    }

    public Task<Student?> FindStudentAsync(int studentId, CancellationToken cancellationToken) =>
        Task.FromResult(Students.FirstOrDefault(student => student.StudentId == studentId));

    public Task<IReadOnlyList<AttendanceHistoryItem>> GetHistoryAsync(int studentId, DateOnly yearStart, DateOnly yearEnd, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AttendanceHistoryItem>>(History);

    public Task<ChronicSnapshot?> GetChronicSnapshotAsync(int studentId, string schoolYear, CancellationToken cancellationToken) =>
        Task.FromResult(Snapshots.TryGetValue(studentId, out var snapshot) ? snapshot : null);
}
