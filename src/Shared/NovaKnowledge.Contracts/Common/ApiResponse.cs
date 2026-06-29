namespace NovaKnowledge.Contracts.Common;

public record ApiResponse<T>(T Data, string? CorrelationId = null);

public record ErrorResponse(string Message, string? CorrelationId = null, string? Code = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public record HealthStatus(string Status, string Service, DateTimeOffset Timestamp);
