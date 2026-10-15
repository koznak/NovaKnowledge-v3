using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Contracts.Search;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Configuration;
using NovaKnowledge.Infrastructure.Ingestion;

namespace NovaKnowledge.IntegrationTests;

/// <summary>
/// Exercises the ingestion pipeline with mocked blob/embedding/search collaborators so the
/// end-to-end control flow (status transitions, chunking, indexing, error handling) is covered
/// without any Azure dependency.
/// </summary>
public class DocumentProcessorTests
{
    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly IBlobStorageService _blobs = Substitute.For<IBlobStorageService>();
    private readonly ITextExtractionService _extraction = Substitute.For<ITextExtractionService>();
    private readonly IChunkingService _chunking = Substitute.For<IChunkingService>();
    private readonly IEmbeddingService _embeddings = Substitute.For<IEmbeddingService>();
    private readonly ISearchIndexService _searchIndex = Substitute.For<ISearchIndexService>();
    private readonly ITenantSettingsRepository _tenantSettings = Substitute.For<ITenantSettingsRepository>();

    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private DocumentProcessor CreateSut(bool hybridSearch)
    {
        var storage = Options.Create(new AzureStorageOptions { DocumentsContainer = "documents" });
        var search = Options.Create(new AzureSearchOptions { EnableHybridSearch = hybridSearch });
        var openAi = Options.Create(new AzureOpenAIOptions { EmbeddingDimensions = 8 });

        return new DocumentProcessor(
            _documents, _blobs, _extraction, _chunking, _embeddings, _searchIndex, _tenantSettings,
            storage, search, openAi, NullLogger<DocumentProcessor>.Instance);
    }

    private void ArrangeHappyPath()
    {
        _documents.GetByIdUntrackedAsync(DocumentId, Arg.Any<CancellationToken>())
            .Returns(new Document
            {
                Id = DocumentId,
                TenantId = "contoso",
                BlobPath = "contoso/policy.pdf",
                ContentType = "application/pdf",
                Title = "HR Policy",
                Department = "HR"
            });
        _blobs.DownloadAsync("documents", "contoso/policy.pdf", Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream([1, 2, 3]));
        _extraction.ExtractTextAsync(Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>())
            .Returns("Employees receive 20 days of PTO annually.");
        _chunking.ChunkText(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(["Employees receive 20 days of PTO annually."]);
        _embeddings.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[8]]);
    }

    [Fact]
    public async Task ProcessAsync_indexes_document_and_marks_indexed()
    {
        ArrangeHappyPath();
        var sut = CreateSut(hybridSearch: true);

        await sut.ProcessAsync(new DocumentUploadedEvent(DocumentId, "contoso", "contoso/policy.pdf", "hash", "Recursive"), CancellationToken.None);

        await _documents.Received(1).SetStatusAsync(DocumentId, DocumentStatus.Processing, null, Arg.Any<CancellationToken>());
        await _documents.Received(1).SetStatusAsync(DocumentId, DocumentStatus.Indexed, null, Arg.Any<CancellationToken>());
        await _searchIndex.Received(1).DeleteByDocumentIdAsync(DocumentId, Arg.Any<CancellationToken>());
        await _searchIndex.Received(1).IndexDocumentsAsync(
            Arg.Is<IReadOnlyList<SearchIndexDocument>>(docs => docs.Count == 1 && docs[0].DocumentId == DocumentId),
            Arg.Any<CancellationToken>());
        await _documents.Received(1).AddChunksAsync(
            Arg.Is<IReadOnlyList<DocumentChunk>>(chunks => chunks.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_keyword_only_skips_embeddings()
    {
        ArrangeHappyPath();
        var sut = CreateSut(hybridSearch: false);

        await sut.ProcessAsync(new DocumentUploadedEvent(DocumentId, "contoso", "contoso/policy.pdf", "hash", "Recursive"), CancellationToken.None);

        await _embeddings.DidNotReceiveWithAnyArgs().GenerateEmbeddingsAsync(default!, default);
        await _documents.Received(1).SetStatusAsync(DocumentId, DocumentStatus.Indexed, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_failure_marks_failed_and_rethrows()
    {
        ArrangeHappyPath();
        _extraction.ExtractTextAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("extraction boom"));
        var sut = CreateSut(hybridSearch: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ProcessAsync(new DocumentUploadedEvent(DocumentId, "contoso", "contoso/policy.pdf", "hash", "Recursive"), CancellationToken.None));

        await _documents.Received(1).SetStatusAsync(DocumentId, DocumentStatus.Failed, "extraction boom", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_missing_document_throws()
    {
        _documents.GetByIdUntrackedAsync(DocumentId, Arg.Any<CancellationToken>()).Returns((Document?)null);
        var sut = CreateSut(hybridSearch: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ProcessAsync(new DocumentUploadedEvent(DocumentId, "contoso", "path", "hash", "Recursive"), CancellationToken.None));
    }
}
