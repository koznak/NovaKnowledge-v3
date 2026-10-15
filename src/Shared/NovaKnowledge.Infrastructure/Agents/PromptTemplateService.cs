using NovaKnowledge.Application.Interfaces;

namespace NovaKnowledge.Infrastructure.Agents;

/// <summary>Resolves the active prompt template for a tenant/category from the repository.</summary>
public sealed class PromptTemplateService(IPromptTemplateRepository repository) : IPromptTemplateService
{
    public Task<Domain.Entities.PromptTemplate?> GetActiveTemplateAsync(
        string category,
        string tenantId,
        CancellationToken cancellationToken = default) =>
        repository.GetActiveAsync(category, tenantId, cancellationToken);
}
