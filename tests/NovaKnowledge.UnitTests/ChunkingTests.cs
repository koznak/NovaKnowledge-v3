using NovaKnowledge.Application.Chunking;

namespace NovaKnowledge.UnitTests;

public class ChunkingTests
{
    private readonly RecursiveChunkingService _sut = new();

    [Fact]
    public void ChunkText_Recursive_SplitsOnParagraphs()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Paragraph {i} with enough content to force splitting when combined together."));
        var chunks = _sut.ChunkText(text, "recursive", 256, 128);
        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Contains("Paragraph 1"));
    }

    [Fact]
    public void ChunkText_Fixed_RespectsOverlap()
    {
        var text = new string('a', 1000);
        var chunks = _sut.ChunkText(text, "fixed", 512, 128);
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Length <= 512));
    }

    [Fact]
    public void ChunkText_Empty_ReturnsEmpty()
    {
        var chunks = _sut.ChunkText("", "recursive");
        Assert.Empty(chunks);
    }

    [Theory]
    [InlineData("recursive")]
    [InlineData("fixed")]
    [InlineData("semantic")]
    public void ChunkText_AllStrategies_ReturnResults(string strategy)
    {
        var text = "Contoso Corp PTO policy grants 20 days annually.\n\nContact HR for details.";
        var chunks = _sut.ChunkText(text, strategy, 256, 64);
        Assert.NotEmpty(chunks);
    }
}
