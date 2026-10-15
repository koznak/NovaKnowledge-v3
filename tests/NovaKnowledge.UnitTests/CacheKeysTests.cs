using NovaKnowledge.Infrastructure.Caching;

namespace NovaKnowledge.UnitTests;

public class CacheKeysTests
{
    [Fact]
    public void Search_is_deterministic_for_identical_inputs()
    {
        var a = CacheKeys.Search("contoso", "PTO policy", 5, true, true);
        var b = CacheKeys.Search("contoso", "PTO policy", 5, true, true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Search_is_case_and_whitespace_insensitive()
    {
        var a = CacheKeys.Search("Contoso", "  PTO Policy ", 5, true, true);
        var b = CacheKeys.Search("contoso", "pto policy", 5, true, true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Search_changes_when_parameters_change()
    {
        var baseline = CacheKeys.Search("contoso", "pto", 5, true, true);

        Assert.NotEqual(baseline, CacheKeys.Search("contoso", "pto", 10, true, true));
        Assert.NotEqual(baseline, CacheKeys.Search("contoso", "pto", 5, false, true));
        Assert.NotEqual(baseline, CacheKeys.Search("other", "pto", 5, true, true));
    }

    [Fact]
    public void Search_key_is_prefixed_and_hex()
    {
        var key = CacheKeys.Search("contoso", "pto", 5, true, true);

        Assert.StartsWith("search:", key, StringComparison.Ordinal);
        Assert.Equal(7 + 64, key.Length);
        Assert.Matches("^search:[0-9a-f]{64}$", key);
    }
}
