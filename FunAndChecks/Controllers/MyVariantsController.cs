using FunAndChecks.Application.Variants;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Варианты студента: просмотр наборов предмета и выбор, пока он открыт.</summary>
[ApiController]
[Route("api/me/variants")]
[Authorize(Roles = Roles.Student)]
public class MyVariantsController(IVariantService variants) : ControllerBase
{
    [HttpGet("subjects/{subjectId:int}")]
    public async Task<ActionResult<List<StudentVariantSetDto>>> Get(int subjectId, CancellationToken ct) =>
        Ok(await variants.GetMyAsync(User.GetUserId(), subjectId, ct));

    [HttpPost("sets/{setId:int}/claim")]
    public async Task<ActionResult<StudentVariantSetDto>> Claim(int setId, AssignVariantRequest request, CancellationToken ct) =>
        Ok(await variants.ClaimAsync(User.GetUserId(), setId, request.VariantId, ct));
}
