using AttendanceSystem.Api.Dtos;
using AttendanceSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Api.Controllers;

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance)
    {
        _attendance = attendance;
    }

    [HttpPost("daily")]
    [ProducesResponseType(typeof(SaveDailyAttendanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SaveDailyAttendanceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDaily(
        [FromBody] SaveDailyAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _attendance.SaveDailyAsync(request, cancellationToken);
        var status = result.Students.Any(student => student.Operation == "Updated")
            ? StatusCodes.Status200OK
            : StatusCodes.Status201Created;
        return StatusCode(status, result);
    }

    [HttpGet("students/{studentId:int}/history")]
    [ProducesResponseType(typeof(AttendanceHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AttendanceHistoryResponse>> GetHistory(
        int studentId,
        [FromQuery] string? schoolYear,
        CancellationToken cancellationToken)
    {
        var result = await _attendance.GetHistoryAsync(studentId, schoolYear, cancellationToken);
        return Ok(result);
    }

    [HttpGet("students/{studentId:int}/chronic-absenteeism")]
    [ProducesResponseType(typeof(ChronicAbsenteeismResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChronicAbsenteeismResponse>> GetChronicAbsenteeism(
        int studentId,
        [FromQuery] string? schoolYear,
        CancellationToken cancellationToken)
    {
        var result = await _attendance.GetChronicAbsenteeismAsync(studentId, schoolYear, cancellationToken);
        return Ok(result);
    }
}
