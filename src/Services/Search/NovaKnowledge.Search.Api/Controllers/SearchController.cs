using Microsoft.AspNetCore.Mvc;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Search;

namespace NovaKnowledge.Search.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ISearchQueryService _searchService;
    private readonly ISearchIndexService _indexService;

    public SearchController(ISearchQueryService searchService, ISearchIndexService indexService)
    {
        _searchService = searchService;
        _indexService = indexService;
    }

    [HttpPost("query")]
    public async Task<ActionResult<ApiResponse<SearchQueryResponse>>> Query(
        [FromBody] SearchQueryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _searchService.SearchAsync(request, cancellationToken);
        return Ok(new ApiResponse<SearchQueryResponse>(response, HttpContext.TraceIdentifier));
    }

    [HttpPost("query/hybrid")]
    public async Task<ActionResult<ApiResponse<SearchQueryResponse>>> HybridQuery(
        [FromBody] SearchQueryRequest request,
        CancellationToken cancellationToken)
    {
        var hybridRequest = request with { UseHybrid = true, UseSemanticRanker = true };
        var response = await _searchService.SearchAsync(hybridRequest, cancellationToken);
        return Ok(new ApiResponse<SearchQueryResponse>(response, HttpContext.TraceIdentifier));
    }

    [HttpPost("index/ensure")]
    public async Task<IActionResult> EnsureIndex(CancellationToken cancellationToken)
    {
        await _indexService.EnsureIndexExistsAsync(cancellationToken);
        return Ok(new { status = "index_ready" });
    }
}
