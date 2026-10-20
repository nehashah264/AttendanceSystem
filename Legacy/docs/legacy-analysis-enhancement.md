# Legacy Attendance System — Database Enhancements

Recommendations for the database behind the daily-attendance system. This note complements [legacy-analysis.md](legacy-analysis.md). It does not restate the VB6 platform or the save and fetch flows. Nothing here has been applied to `schema.sql` or the procedures. Lengths on the new string columns are proposals: the call sites name the columns and do not declare a width.

`schema.sql` creates `Students`, `AttendanceCodes`, `StudentAttendance`, `StudentAttendanceSummary`, and `StudentAlerts`. It creates no foreign keys and no indexes beyond the primary keys, the unique `CodeValue`, and `UQ_Summary (StudentID, SchoolYear)`.

## 1. Missing tables

Three tables are required by the form or the procedures and are not created anywhere under `Legacy/`.

| Table | Call site | Columns the code reads or writes |
| --- | --- | --- |
| `Schools` | `LoadSchools`, `sp_SaveDailyAttendance`, `sp_GetStudentAttendance` | `SchoolID`, `SchoolName`, `Active`, `AbsenceAlertThreshold` |
| `SchoolTerms` | Term lookup in the save procedure; left join in the get procedure | `TermID`, `SchoolID`, `StartDate`, `EndDate`, `TermName` |
| `AttendanceSubmissionLog` | Final insert of `sp_SaveDailyAttendance` | `SchoolID`, `SubmittedDate`, `AttendDate`, `RecordCount`, `SubmittedBy` |

The submission insert does not name a key. The proposal adds `LogID`.

```sql
CREATE TABLE Schools (
    SchoolID               INT          IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SchoolName             VARCHAR(200) NOT NULL,
    Active                 BIT          NOT NULL DEFAULT 1,
    AbsenceAlertThreshold  INT          NULL
);

CREATE TABLE SchoolTerms (
    TermID     INT          IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SchoolID   INT          NOT NULL,
    TermName   VARCHAR(100) NOT NULL,
    StartDate  DATE         NOT NULL,
    EndDate    DATE         NOT NULL,
    CONSTRAINT FK_SchoolTerms_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID)
);

CREATE TABLE AttendanceSubmissionLog (
    LogID          INT          IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SchoolID       INT          NOT NULL,
    SubmittedDate  DATETIME     NOT NULL,
    AttendDate     DATE         NOT NULL,
    RecordCount    INT          NOT NULL,
    SubmittedBy    VARCHAR(100) NULL,
    CONSTRAINT FK_AttendanceSubmissionLog_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID)
);
```

Once `Schools` and `SchoolTerms` exist, the existing tables can reference them. `TermID` stays nullable because a date with no matching term is stored as NULL.

```sql
ALTER TABLE Students
    ADD CONSTRAINT FK_Students_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID);

ALTER TABLE StudentAttendance
    ADD CONSTRAINT FK_StudentAttendance_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID);

ALTER TABLE StudentAttendance
    ADD CONSTRAINT FK_StudentAttendance_SchoolTerms
        FOREIGN KEY (TermID) REFERENCES SchoolTerms (TermID);

ALTER TABLE StudentAttendanceSummary
    ADD CONSTRAINT FK_StudentAttendanceSummary_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID);

ALTER TABLE StudentAlerts
    ADD CONSTRAINT FK_StudentAlerts_Schools
        FOREIGN KEY (SchoolID) REFERENCES Schools (SchoolID);
```

`SchoolTerms (SchoolID, StartDate, EndDate)` does not stop two terms for the same school from overlapping. The save procedure assigns `SELECT @TermID = TermID` with no `ORDER BY`, so an overlap still picks an arbitrary term. The index in section 2 makes that lookup cheap. It does not make it deterministic.

### Related objects that are not tables

Two routines are also missing. Creating the three tables does not make the database runnable.

- `sp_GetStudentsForAttendance` is what `LoadStudents` executes, with school id, grade text, and `yyyy-mm-dd`. The form reads `StudentID`, `LastName`, `FirstName`, `AttendCode`, `MinutesLate`, and `Notes`. The body is not in the repo, so this note does not invent it.
- `SchoolYear()` is called in the absence recount and is not created in `schema.sql`. Do not add that scalar function. The argument today is the batch `@AttendDate`, not each row’s `AttendDate`, so a faithful function would still count every year. Filter `AttendDate` with a range instead:

```sql
-- September-through-August year that contains @AttendDate
DECLARE @YearStart date, @YearEnd date;

IF MONTH(@AttendDate) >= 9
BEGIN
    SET @YearStart = DATEFROMPARTS(YEAR(@AttendDate), 9, 1);
    SET @YearEnd   = DATEFROMPARTS(YEAR(@AttendDate) + 1, 8, 31);
END
ELSE
BEGIN
    SET @YearStart = DATEFROMPARTS(YEAR(@AttendDate) - 1, 9, 1);
    SET @YearEnd   = DATEFROMPARTS(YEAR(@AttendDate), 8, 31);
END
```

The recount predicate becomes `StudentID`, `SchoolID`, `AttendDate` between `@YearStart` and `@YearEnd`, and `IsAbsent = 1`. The same range should replace the `CASE` expression in `sp_GetStudentAttendance`, which is why the attendance index below can be used.

## 2. Indexes

### Already covered

| Lookup | Support already in the schema |
| --- | --- |
| `AttendanceCodes` by `CodeValue` | Unique constraint on `CodeValue` |
| Summary upsert and get by `StudentID`, `SchoolYear` | `UQ_Summary (StudentID, SchoolYear)` |
| `Schools` by `SchoolID` for the threshold | Primary key, once `Schools` exists |
| `SchoolTerms` by `TermID` for the get left join | Primary key, once `SchoolTerms` exists |

No statement filters `DateOfBirth`, `CreatedDate`, or `CodeID`. Those columns do not need indexes.

Primary keys on the identity columns stay the clustered indexes. The keys below are nonclustered. SQL Server already carries the clustered key in every nonclustered index, so `SchoolID` does not need to be added to the school-name index just to satisfy `SELECT SchoolID, SchoolName`.

### Add for statements that exist today

`LoadGrades` filters `Students` by `SchoolID` and `Active = 1` and orders by `Grade`.

```sql
CREATE NONCLUSTERED INDEX IX_Students_School_Active_Grade
    ON Students (SchoolID, Active, Grade);
```

`LoadSchools` filters `Active = 1` and orders by `SchoolName`.

```sql
CREATE NONCLUSTERED INDEX IX_Schools_Active_SchoolName
    ON Schools (Active, SchoolName);
```

The save cursor looks up one day with `StudentID` and `AttendDate`. The get procedure returns one student ordered by `AttendDate DESC`. There is no unique constraint on that pair, so two sessions can insert two rows and a later `SELECT @ExistingID = AttendanceID` can pick either. The unique index is the integrity rule and the lookup path.

```sql
CREATE UNIQUE NONCLUSTERED INDEX UX_StudentAttendance_Student_Date
    ON StudentAttendance (StudentID, AttendDate);
```

Build this only after duplicate `(StudentID, AttendDate)` rows are removed. The index creation will fail while duplicates remain.

The get procedure will use that index for the student and the date order only after the school-year `CASE` on `AttendDate` is replaced with `AttendDate` between `@YearStart` and `@YearEnd`. As written, the `CASE` is applied to every row for the student and the date predicate is not sargable.

The recount, after the date-range change above, is `StudentID`, `SchoolID`, a date range, and `IsAbsent = 1`. `IsAbsent` stays in `INCLUDE` because a range seek on `AttendDate` cannot also seek a separate flag efficiently. The unique `(StudentID, AttendDate)` index does not contain `SchoolID` or `IsAbsent`.

```sql
CREATE NONCLUSTERED INDEX IX_StudentAttendance_Recount
    ON StudentAttendance (StudentID, SchoolID, AttendDate)
    INCLUDE (IsAbsent);
```

Term resolution is `SchoolID = @SchoolID AND @AttendDate BETWEEN StartDate AND EndDate`.

```sql
CREATE NONCLUSTERED INDEX IX_SchoolTerms_School_Dates
    ON SchoolTerms (SchoolID, StartDate, EndDate)
    INCLUDE (TermID);
```

The unresolved-alert probe is `StudentID`, `AlertType = 'CHRONIC_ABSENCE'`, `SchoolYear`, and `ResolvedDate IS NULL`.

```sql
CREATE NONCLUSTERED INDEX IX_StudentAlerts_OpenAlert
    ON StudentAlerts (StudentID, SchoolYear, AlertType)
    INCLUDE (ResolvedDate);
```

A filtered index is the tighter form of the same probe, if the alert volume justifies a second object. It only helps the unresolved check. A query that also reads resolved alerts would use the index above.

```sql
CREATE NONCLUSTERED INDEX IX_StudentAlerts_OpenAlert_Filtered
    ON StudentAlerts (StudentID, SchoolYear, AlertType)
    WHERE ResolvedDate IS NULL;
```

Pick one of the two alert indexes, not both, unless measurement shows the filtered index earning its extra write cost.

### Conditional roster index

`sp_GetStudentsForAttendance` is not in the repo. If its body left-joins `StudentAttendance` for one school and one date, and returns the code, minutes, and notes the grid displays, this covering index matches that join:

```sql
CREATE NONCLUSTERED INDEX IX_StudentAttendance_School_Date
    ON StudentAttendance (SchoolID, AttendDate)
    INCLUDE (StudentID, AttendCode, MinutesLate, Notes);
```

Skip it until the procedure body is known. `IX_Students_School_Active_Grade` already serves a roster filter on school, active, and grade even without that join.

## 3. Additional schema improvements

### Foreign keys from the existing tables

Section 1 adds the keys that point at `Schools` and `SchoolTerms`. The remaining keys point at `Students` and `AttendanceCodes`. `AttendCode` references `CodeValue`, which is already unique.

```sql
ALTER TABLE StudentAttendance
    ADD CONSTRAINT FK_StudentAttendance_Students
        FOREIGN KEY (StudentID) REFERENCES Students (StudentID);

ALTER TABLE StudentAttendanceSummary
    ADD CONSTRAINT FK_StudentAttendanceSummary_Students
        FOREIGN KEY (StudentID) REFERENCES Students (StudentID);

ALTER TABLE StudentAlerts
    ADD CONSTRAINT FK_StudentAlerts_Students
        FOREIGN KEY (StudentID) REFERENCES Students (StudentID);

ALTER TABLE StudentAttendance
    ADD CONSTRAINT FK_StudentAttendance_AttendanceCodes
        FOREIGN KEY (AttendCode) REFERENCES AttendanceCodes (CodeValue);
```

Use `ON DELETE NO ACTION` and `ON UPDATE NO ACTION` on the code key. `IsAbsent` and `IsExcused` on `StudentAttendance` are a snapshot taken at save time. A later change to the code row must not rewrite history, and renaming `CodeValue` must not cascade into old attendance rows.

### Checks

`CHECK (MinutesLate >= 0)` still allows NULL, which matches the column. The school-year check matches the `YYYY-YYYY` values both procedures build.

```sql
ALTER TABLE StudentAttendance
    ADD CONSTRAINT CK_StudentAttendance_MinutesLate
        CHECK (MinutesLate >= 0);

ALTER TABLE StudentAttendanceSummary
    ADD CONSTRAINT CK_Summary_SchoolYear
        CHECK (SchoolYear LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]');

ALTER TABLE StudentAlerts
    ADD CONSTRAINT CK_Alerts_SchoolYear
        CHECK (SchoolYear LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]');
```

### Seed the form default

The form writes `P` when the code is blank. `schema.sql` inserts no codes, so `P` is unknown and the save procedure stores absence flags from a failed lookup. Seed the default before the code foreign key is added.

```sql
INSERT INTO AttendanceCodes (CodeValue, Description, IsAbsent, IsExcused, IsActive)
VALUES ('P', 'Present', 0, 0, 1);
```

### Measure after the write path changes

The save procedure recounts and looks up codes once per student inside a cursor. Replace that cursor with a set-based upsert before re-measuring these indexes. A plan taken on the cursor will overstate how often the per-student seeks run and will hide the cost of one set-based pass.
