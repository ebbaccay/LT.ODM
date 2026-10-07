using LT.ODM.Application.Ai;
using LT.ODM.Application.CostOptimization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Cost Optimization (ported from TMS): AI savings ideas for a style. Signed-in users only.
/// Sessions, suggestions and reviews still use the TMS procedures through /hubs/sp.
/// </summary>
[ApiController]
[Route("api/v1/cost-optimization")]
public sealed class CostOptimizationController(ICostSuggestionAi ai) : ControllerBase
{
    /// <summary>Replaces the TMS hub method StreamCostOptimizationSuggestions. The screen saves the ideas to the session.</summary>
    [HttpPost("suggestions")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<IReadOnlyList<CostSuggestionDto>>> Suggest(CostSuggestionRequest request, CancellationToken ct)
    {
        var errors = CostOptimizationRules.Suggestions(request);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        if (!await ai.IsConfiguredAsync(ct))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI suggestions are not set up on this server. Ask your administrator to set the Text job in Settings > AI connections.");
        try
        {
            return Ok(await ai.SuggestAsync(request, ct));
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }
}
