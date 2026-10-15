using System.Security.Cryptography;
using System.Text;

namespace NovaKnowledge.Infrastructure.Caching;

/// <summary>
/// Builds stable, process-independent cache keys.
/// <see cref="object.GetHashCode"/> on strings is randomized per process, so it must never be
/// used as a cache key — this produces a deterministic SHA-256 based key instead.
/// </summary>
public static class CacheKeys
{
    public static string Search(string tenantId, string query, int topK, bool useHybrid, bool useSemanticRanker)
    {
        var normalized = string.Join('\u001f',
            tenantId.Trim().ToLowerInvariant(),
            query.Trim().ToLowerInvariant(),
            topK.ToString(),
            useHybrid,
            useSemanticRanker);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"search:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
