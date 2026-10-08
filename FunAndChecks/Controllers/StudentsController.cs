using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/students")]
public class StudentsController(
    IStudentService studentService,
    ISubjectService subjectService,
    IGradeService gradeService)
    : ControllerBase
{
    /// <summary>Поиск студентов по фамилии/имени. Пустой запрос → все студенты (по алфавиту).</summary>
    [HttpGet("search")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<StudentDetailsDto>>> Search([FromQuery] string? query, CancellationToken cancellationToken) =>
        Ok(await studentService.SearchStudentsAsync(User.GetUserId(), query ?? string.Empty, cancellationToken));

    /// <summary>Публичная карточка студента.</summary>
    [HttpGet("{studentId:guid}")]
    public async Task<ActionResult<StudentDto>> Get(Guid studentId, CancellationToken cancellationToken) =>
        Ok(await studentService.GetAsync(studentId, cancellationToken));

    /// <summary>Задать цвет заливки ячейки студента в таблице результатов.</summary>
    [HttpPut("{studentId:guid}/color")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetColor(Guid studentId, SetStudentColorRequest request, CancellationToken cancellationToken)
    {
        await studentService.SetColorAsync(User.GetUserId(), studentId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Полная карточка студента (контакты, GitHub).</summary>
    [HttpGet("{studentId:guid}/details")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<StudentDetailsDto>> GetDetails(Guid studentId, CancellationToken cancellationToken) =>
        Ok(await studentService.GetDetailsAsync(User.GetUserId(), studentId, cancellationToken));

    /// <summary>Задания предмета со статусами сдачи конкретного студента.</summary>
    [HttpGet("{studentId:guid}/subjects/{subjectId:int}/tasks")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<TaskWithStatusDto>>> GetTasksWithStatus(
        Guid studentId, int subjectId, CancellationToken cancellationToken) =>
        Ok(await subjectService.GetTasksWithStatusAsync(subjectId, studentId, cancellationToken));

    /// <summary>Оценки студента по глобальным колонкам предмета (билет, курсовая).</summary>
    [HttpGet("{studentId:guid}/subjects/{subjectId:int}/grades")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<StudentGradeDto>>> GetGrades(
        Guid studentId, int subjectId, CancellationToken cancellationToken) =>
        Ok(await gradeService.GetStudentGradesAsync(User.GetUserId(), studentId, subjectId, cancellationToken));

    /// <summary>Редактирование профиля и аккаунта студента.</summary>
    [HttpPut("{studentId:guid}/account")]
    [Authorize(Roles = Roles.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAccount(Guid studentId, UpdateStudentAccountRequest request, CancellationToken cancellationToken)
    {
        await studentService.UpdateStudentAccountAsync(User.GetUserId(), studentId, request, cancellationToken);
        return NoContent();
    }
}
