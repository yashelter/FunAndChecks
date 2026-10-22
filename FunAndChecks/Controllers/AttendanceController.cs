using FunAndChecks.Application.Attendance;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Журнал посещаемости и ручная перекличка преподавателем.</summary>
[ApiController]
[Route("api/attendance")]
[Authorize(Roles = Roles.Admin)]
public class AttendanceController(IAttendanceService attendance) : ControllerBase
{
    [HttpPut("subjects/{subjectId:int}/settings")]
    public async Task<IActionResult> SetEnabled(int subjectId, AttendanceSettingsDto request, CancellationToken ct)
    {
        await attendance.SetEnabledAsync(User.GetUserId(), subjectId, request.Enabled, ct);
        return NoContent();
    }

    [HttpGet("subjects/{subjectId:int}/groups")]
    public async Task<IActionResult> GetGroups(int subjectId, CancellationToken ct) =>
        Ok(await attendance.GetGroupsAsync(User.GetUserId(), subjectId, ct));

    [HttpGet("subjects/{subjectId:int}/journal")]
    public async Task<ActionResult<AttendanceJournalDto>> GetJournal(int subjectId, [FromQuery] int? groupId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await attendance.GetJournalAsync(User.GetUserId(), subjectId, groupId, from, to, ct));

    [HttpGet("subjects/{subjectId:int}/export")]
    public async Task<IActionResult> Export(int subjectId, [FromQuery] int? groupId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var journal = await attendance.GetJournalAsync(User.GetUserId(), subjectId, groupId, from, to, ct);
        return File(AttendanceXlsxExporter.Build(journal),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Attendance_{subjectId}.xlsx");
    }

    [HttpPost("subjects/{subjectId:int}/sessions")]
    public async Task<ActionResult<AttendanceSessionDto>> CreateSession(int subjectId,
        CreateAttendanceSessionRequest request, CancellationToken ct)
    {
        var session = await attendance.CreateSessionAsync(User.GetUserId(), subjectId, request, ct);
        return CreatedAtAction(nameof(GetSession), new { sessionId = session.Id }, session);
    }

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<ActionResult<AttendanceSessionDetailsDto>> GetSession(int sessionId, CancellationToken ct) =>
        Ok(await attendance.GetSessionAsync(User.GetUserId(), sessionId, ct));

    [HttpPut("sessions/{sessionId:int}/students/{studentId:guid}")]
    public async Task<ActionResult<AttendanceParticipantDto>> SetMark(int sessionId, Guid studentId,
        SetAttendanceRequest request, CancellationToken ct) =>
        Ok(await attendance.SetMarkAsync(User.GetUserId(), sessionId, studentId, request, ct));

    [HttpPost("sessions/{sessionId:int}/remaining-absent")]
    public async Task<IActionResult> MarkRemaining(int sessionId, RemainingAttendanceRequest request, CancellationToken ct)
    {
        await attendance.MarkRemainingAbsentAsync(User.GetUserId(), sessionId, request, ct);
        return NoContent();
    }

    [HttpPost("sessions/{sessionId:int}/students/{studentId:guid}")]
    public async Task<IActionResult> AddStudent(int sessionId, Guid studentId, CancellationToken ct)
    {
        await attendance.AddStudentAsync(User.GetUserId(), sessionId, studentId, ct);
        return NoContent();
    }

    [HttpGet("sessions/{sessionId:int}/available-students")]
    public async Task<IActionResult> GetAvailableStudents(int sessionId, CancellationToken ct) =>
        Ok(await attendance.GetAvailableStudentsAsync(User.GetUserId(), sessionId, ct));
}
