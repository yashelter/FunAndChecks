using FunAndChecks.Application.Variants;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Наборы вариантов предмета и их выдача преподавателем.</summary>
[ApiController]
[Route("api/variants")]
[Authorize(Roles = Roles.Admin)]
public class VariantsController(IVariantService variants) : ControllerBase
{
    [HttpGet("subjects/{subjectId:int}/sets")]
    public async Task<ActionResult<List<VariantSetDto>>> GetSets(int subjectId, CancellationToken ct) =>
        Ok(await variants.GetSetsAsync(User.GetUserId(), subjectId, ct));

    [HttpPost("subjects/{subjectId:int}/sets")]
    public async Task<ActionResult<VariantSetDto>> CreateSet(int subjectId, SaveVariantSetRequest request, CancellationToken ct)
    {
        var set = await variants.CreateSetAsync(User.GetUserId(), subjectId, request, ct);
        return CreatedAtAction(nameof(GetSets), new { subjectId }, set);
    }

    [HttpPut("sets/{setId:int}")]
    public async Task<ActionResult<VariantSetDto>> UpdateSet(int setId, SaveVariantSetRequest request, CancellationToken ct) =>
        Ok(await variants.UpdateSetAsync(User.GetUserId(), setId, request, ct));

    [HttpDelete("sets/{setId:int}")]
    public async Task<IActionResult> DeleteSet(int setId, CancellationToken ct)
    {
        await variants.DeleteSetAsync(User.GetUserId(), setId, ct);
        return NoContent();
    }

    [HttpPost("sets/{setId:int}/variants")]
    public async Task<ActionResult<VariantDto>> AddVariant(int setId, AddVariantRequest request, CancellationToken ct) =>
        Ok(await variants.AddVariantAsync(User.GetUserId(), setId, request, ct));

    [HttpPost("sets/{setId:int}/variants/range")]
    public async Task<ActionResult<int>> AddRange(int setId, AddVariantRangeRequest request, CancellationToken ct) =>
        Ok(await variants.AddVariantRangeAsync(User.GetUserId(), setId, request, ct));

    [HttpPut("variants/{variantId:int}")]
    public async Task<ActionResult<VariantDto>> UpdateVariant(int variantId, UpdateVariantRequest request, CancellationToken ct) =>
        Ok(await variants.UpdateVariantAsync(User.GetUserId(), variantId, request, ct));

    [HttpDelete("variants/{variantId:int}")]
    public async Task<IActionResult> DeleteVariant(int variantId, CancellationToken ct)
    {
        await variants.DeleteVariantAsync(User.GetUserId(), variantId, ct);
        return NoContent();
    }

    [HttpGet("subjects/{subjectId:int}/board")]
    public async Task<ActionResult<VariantBoardDto>> GetBoard(int subjectId, CancellationToken ct) =>
        Ok(await variants.GetBoardAsync(User.GetUserId(), subjectId, ct));

    [HttpGet("subjects/{subjectId:int}/students/{studentId:guid}")]
    public async Task<ActionResult<List<StudentVariantSetDto>>> GetForStudent(int subjectId, Guid studentId, CancellationToken ct) =>
        Ok(await variants.GetForStudentAsync(User.GetUserId(), subjectId, studentId, ct));

    [HttpPut("sets/{setId:int}/students/{studentId:guid}")]
    public async Task<IActionResult> Assign(int setId, Guid studentId, AssignVariantRequest request, CancellationToken ct)
    {
        await variants.AssignAsync(User.GetUserId(), setId, studentId, request.VariantId, ct);
        return NoContent();
    }

    [HttpDelete("sets/{setId:int}/students/{studentId:guid}")]
    public async Task<IActionResult> Unassign(int setId, Guid studentId, CancellationToken ct)
    {
        await variants.UnassignAsync(User.GetUserId(), setId, studentId, ct);
        return NoContent();
    }
}
