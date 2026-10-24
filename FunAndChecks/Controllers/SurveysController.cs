using FunAndChecks.Application.Surveys;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Прохождение опросов студентом.</summary>
[ApiController]
[Authorize]
public class SurveysController(ISurveyService surveys) : ControllerBase
{
    [HttpGet("api/surveys/pending")]
    public async Task<ActionResult<IReadOnlyList<PendingSurveyDto>>> Pending(CancellationToken ct) =>
        Ok(await surveys.GetPendingAsync(User.GetUserId(), ct));

    [HttpGet("api/surveys/{surveyId:int}")]
    public async Task<ActionResult<SurveyFormDto>> Form(int surveyId, CancellationToken ct) =>
        Ok(await surveys.GetFormAsync(User.GetUserId(), surveyId, ct));

    [HttpPost("api/surveys/{surveyId:int}/responses")]
    public async Task<IActionResult> Submit(int surveyId, SubmitSurveyRequest request, CancellationToken ct)
    {
        await surveys.SubmitAsync(User.GetUserId(), surveyId, request, ct);
        return NoContent();
    }

    [HttpPost("api/surveys/{surveyId:int}/snooze")]
    public async Task<IActionResult> Snooze(int surveyId, CancellationToken ct)
    {
        await surveys.SnoozeAsync(User.GetUserId(), surveyId, ct);
        return NoContent();
    }

    [HttpGet("api/me/surveys")]
    public async Task<ActionResult<IReadOnlyList<MySurveyDto>>> Completed(CancellationToken ct) =>
        Ok(await surveys.GetCompletedAsync(User.GetUserId(), ct));
}

/// <summary>Конструктор опросов и итоги для преподавателей.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/surveys")]
public class AdminSurveysController(ISurveyAdminService surveys) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SurveyPageDto>> List([FromQuery] SurveyStatus? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await surveys.GetAsync(User.GetUserId(), status, page, pageSize, ct));

    [HttpPost]
    public async Task<ActionResult<SurveyAdminDto>> Create(SaveSurveyRequest request, CancellationToken ct)
    {
        var survey = await surveys.CreateAsync(User.GetUserId(), request, ct);
        return Created($"/api/admin/surveys/{survey.Id}", survey);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SurveyAdminDto>> Get(int id, CancellationToken ct) =>
        Ok(await surveys.GetByIdAsync(User.GetUserId(), id, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SurveyAdminDto>> Save(int id, SaveSurveyRequest request, CancellationToken ct) =>
        Ok(await surveys.SaveDraftAsync(User.GetUserId(), id, request, ct));

    [HttpPatch("{id:int}/details")]
    public async Task<ActionResult<SurveyAdminDto>> UpdateDetails(int id, UpdateSurveyDetailsRequest request, CancellationToken ct) =>
        Ok(await surveys.UpdateDetailsAsync(User.GetUserId(), id, request, ct));

    [HttpPost("{id:int}/publish")]
    public async Task<ActionResult<SurveyAdminDto>> Publish(int id, CancellationToken ct) =>
        Ok(await surveys.PublishAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:int}/close")]
    public async Task<ActionResult<SurveyAdminDto>> Close(int id, CancellationToken ct) =>
        Ok(await surveys.CloseAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:int}/duplicate")]
    public async Task<ActionResult<SurveyAdminDto>> Duplicate(int id, CancellationToken ct) =>
        Ok(await surveys.DuplicateAsync(User.GetUserId(), id, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await surveys.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/results")]
    public async Task<ActionResult<SurveyResultsDto>> Results(int id, [FromQuery] int? groupId, CancellationToken ct) =>
        Ok(await surveys.GetResultsAsync(User.GetUserId(), id, groupId, ct));

    [HttpGet("{id:int}/responses")]
    public async Task<ActionResult<IReadOnlyList<SurveyIndividualResponseDto>>> Responses(int id, [FromQuery] int? groupId, CancellationToken ct) =>
        Ok(await surveys.GetResponsesAsync(User.GetUserId(), id, groupId, ct));

    [HttpGet("{id:int}/export.xlsx")]
    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        var survey = await surveys.GetByIdAsync(User.GetUserId(), id, ct);
        var responses = await surveys.GetResponsesAsync(User.GetUserId(), id, ct: ct);
        return File(SurveyXlsxExporter.Build(survey, responses),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"survey-{id}.xlsx");
    }
}
