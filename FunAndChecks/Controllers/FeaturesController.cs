using FunAndChecks.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Controllers;

/// <summary>
/// Feature flags exposed to the frontend.
/// </summary>
[ApiController]
[Route("api/features")]
public class FeaturesController(IOptions<FeatureFlags> flags) : ControllerBase
{
    /// <summary>
    /// Get current feature flag values.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FeaturesDto), StatusCodes.Status200OK)]
    public ActionResult<FeaturesDto> Get() => Ok(new FeaturesDto(flags.Value.PrivacyPolicy, flags.Value.HalloweenEvent));
}