using FunAndChecks.Application.Attendance;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/me/attendance")]
[Authorize(Roles = Roles.Student)]
public class MyAttendanceController(IAttendanceService attendance) : ControllerBase
{
    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects(CancellationToken ct) =>
        Ok(await attendance.GetStudentSubjectsAsync(User.GetUserId(), ct));

    [HttpGet("subjects/{subjectId:int}")]
    public async Task<ActionResult<List<StudentAttendanceDto>>> GetHistory(int subjectId, CancellationToken ct) =>
        Ok(await attendance.GetStudentHistoryAsync(User.GetUserId(), subjectId, ct));
}
