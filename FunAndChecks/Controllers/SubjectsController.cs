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
[Route("api/subjects")]
public class SubjectsController(
    ISubjectService subjectService,
    IStudentService studentService,
    IGradeService gradeService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SubjectDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await subjectService.GetAllAsync(cancellationToken));

    [HttpGet("{subjectId:int}")]
    public async Task<ActionResult<SubjectDto>> Get(int subjectId, CancellationToken cancellationToken) =>
        Ok(await subjectService.GetAsync(subjectId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(SubjectDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SubjectDto>> Create(CreateSubjectRequest request, CancellationToken cancellationToken)
    {
        var subject = await subjectService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { subjectId = subject.Id }, subject);
    }

    /// <summary>Удаляет предмет каскадно вместе с заданиями и историей сдач.</summary>
    [HttpDelete("{subjectId:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int subjectId, CancellationToken cancellationToken)
    {
        await subjectService.DeleteAsync(subjectId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{subjectId:int}/tasks")]
    public async Task<ActionResult<List<TaskDto>>> GetTasks(int subjectId, CancellationToken cancellationToken) =>
        Ok(await subjectService.GetTasksAsync(subjectId, cancellationToken));

    [HttpPost("{subjectId:int}/tasks")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TaskDto>> CreateTask(int subjectId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await subjectService.CreateTaskAsync(subjectId, request, cancellationToken);
        return CreatedAtAction(nameof(GetTasks), new { subjectId }, task);
    }

    /// <summary>Все студенты, чьи группы имеют доступ к предмету (для админа).</summary>
    [HttpGet("{subjectId:int}/students")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<StudentDetailsDto>>> GetStudents(int subjectId, CancellationToken cancellationToken) =>
        Ok(await studentService.GetStudentsBySubjectAsync(subjectId, cancellationToken));

    /// <summary>Оценочные колонки предмета (билет, курсовая и т.п.).</summary>
    [HttpGet("{subjectId:int}/grade-components")]
    public async Task<ActionResult<List<GradeComponentDto>>> GetGradeComponents(int subjectId, CancellationToken cancellationToken) =>
        Ok(await gradeService.GetComponentsAsync(subjectId, cancellationToken));

    /// <summary>Добавить предмету оценочную колонку.</summary>
    [HttpPost("{subjectId:int}/grade-components")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(GradeComponentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<GradeComponentDto>> CreateGradeComponent(
        int subjectId, CreateGradeComponentRequest request, CancellationToken cancellationToken)
    {
        var component = await gradeService.CreateComponentAsync(User.GetUserId(), subjectId, request, cancellationToken);
        return CreatedAtAction(nameof(GetGradeComponents), new { subjectId }, component);
    }
}
