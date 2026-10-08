using LT.ODM.Application.Ai;
using LT.ODM.Application.MarketTrends;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Market Trends (ported from TMS product-trends): AI trend read-out for a collection. Signed-in users only.
/// Sessions and saved scores still use the TMS procedures through /hubs/sp.
/// </summary>
[ApiController]
[Route("api/v1/market-trends")]
public sealed class MarketTrendsController(ITrendAnalysisAi ai) : ControllerBase
{
    /// <summary>Replaces the TMS hub method StreamCollectionTrendAnalysis. The screen saves the result to the session.</summary>
    [HttpPost("analysis")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<TrendAnalysisDto>> Analyze(TrendAnalysisRequest request, CancellationToken ct)
    {
        var errors = MarketTrendRules.Analysis(request);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        if (!await ai.IsConfiguredAsync(ct))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: AiUnavailable.Generic("AI trend analysis"));
        try
        {
            return await ai.AnalyzeAsync(request, ct);
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }
}
