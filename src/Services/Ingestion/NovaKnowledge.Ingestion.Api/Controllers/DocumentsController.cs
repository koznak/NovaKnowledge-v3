using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NovaKnowledge.Application.Interfaces;
using NovaKnowledge.Contracts.Common;
using NovaKnowledge.Contracts.Ingestion;
using NovaKnowledge.Domain.Entities;
using NovaKnowledge.Infrastructure.Azure;
using NovaKnowledge.Infrastructure.Configuration;
using NovaKnowledge.Infrastructure.Ingestion;

namespace NovaKnowledge.Ingestion.Api.Controllers;

[ApiController]
[Route("api/ingest")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _documents;
    private readonly IBlobStorageService _blobStorage;
    private readonly IMessagePublisher _publisher;
    private readonly AzureStorageOptions _storageOptions;
    private readonly NovaKnowledgeOptions _novaOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly DocumentProcessor _documentProcessor;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain",
        "text/markdown"
    };

    public DocumentsController(
        IDocumentRepository documents,
        IBlobStorageService blobStorage,
        IMessagePublisher publisher,
        Microsoft.Extensions.Options.IOptions<AzureStorageOptions> storageOptions,
        Microsoft.Extensions.Options.IOptions<NovaKnowledgeOptions> novaOptions,
        IWebHostEnvironment environment,
        DocumentProcessor documentProcessor)
    {
        _documents = documents;
        _blobStorage = blobStorage;
        _publisher = publisher;
        _storageOptions = storageOptions.Value;
        _novaOptions = novaOptions.Value;
        _environment = environment;
        _documentProcessor = documentProcessor;
    }

    [HttpPost("documents")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<ApiResponse<DocumentResponse>>> Upload(
        IFormFile file,
        [FromForm] UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ErrorResponse("File is required"));

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new ErrorResponse($"Unsupported content type: {file.ContentType}"));

        await using var stream = file.OpenReadStream();
        var hash = AzureBlobStorageService.ComputeContentHash(stream);
        stream.Position = 0;

        var tenantId = _novaOptions.DefaultTenantId;
        var existing = await _documents.GetByContentHashAsync(hash, tenantId, cancellationToken);
        if (existing != null)
            return Ok(new ApiResponse<DocumentResponse>(MapDocument(existing), HttpContext.TraceIdentifier));

        if (!Enum.TryParse<ChunkingStrategy>(request.ChunkingStrategy, true, out var strategy))
            strategy = ChunkingStrategy.Recursive;

        var documentId = Guid.NewGuid();
        var blobPath = $"{tenantId}/{documentId}/{file.FileName}";

        await _blobStorage.UploadAsync(_storageOptions.DocumentsContainer, blobPath, stream, file.ContentType, cancellationToken);

        var document = new Document
        {
            Id = documentId,
            TenantId = tenantId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            BlobPath = blobPath,
            ContentHash = hash,
            SizeBytes = file.Length,
            Title = request.Title ?? file.FileName,
            Department = request.Department,
            DocType = request.DocType,
            EffectiveDate = request.EffectiveDate,
            ChunkingStrategy = strategy,
            Status = DocumentStatus.Pending
        };

        await _documents.AddAsync(document, cancellationToken);
        await _documents.SaveChangesAsync(cancellationToken);

        await _publisher.PublishDocumentUploadedAsync(new DocumentUploadedEvent(
            document.Id, tenantId, blobPath, hash, strategy.ToString()), cancellationToken);

        return Accepted(new ApiResponse<DocumentResponse>(MapDocument(document), HttpContext.TraceIdentifier));
    }

    /// <summary>Development-only: process a pending document without Service Bus.</summary>
    [HttpPost("documents/{id:guid}/process")]
    public async Task<ActionResult<ApiResponse<DocumentResponse>>> ProcessDocument(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var document = await _documents.GetByIdAsync(id, cancellationToken);
        if (document == null) return NotFound(new ErrorResponse("Document not found"));

        await _documentProcessor.ProcessAsync(new DocumentUploadedEvent(
            document.Id, document.TenantId, document.BlobPath, document.ContentHash,
            document.ChunkingStrategy.ToString()), cancellationToken);

        var updated = await _documents.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<DocumentResponse>(MapDocument(updated!), HttpContext.TraceIdentifier));
    }

    [HttpGet("documents/{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<DocumentStatusResponse>>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(id, cancellationToken);
        if (document == null) return NotFound(new ErrorResponse("Document not found"));

        return Ok(new ApiResponse<DocumentStatusResponse>(
            new DocumentStatusResponse(id, document.Status.ToString(), document.Chunks.Count, document.ErrorMessage),
            HttpContext.TraceIdentifier));
    }

    [HttpGet("documents")]
    public async Task<ActionResult<ApiResponse<PagedResult<DocumentResponse>>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var items = await _documents.ListAsync(_novaOptions.DefaultTenantId, page, pageSize, cancellationToken);
        return Ok(new ApiResponse<PagedResult<DocumentResponse>>(
            new PagedResult<DocumentResponse>(items.Select(MapDocument).ToList(), items.Count, page, pageSize),
            HttpContext.TraceIdentifier));
    }

    private static DocumentResponse MapDocument(Document d) => new(
        d.Id, d.FileName, d.Status.ToString(), d.Title, d.Department, d.DocType,
        d.EffectiveDate, d.ChunkingStrategy.ToString(), d.CreatedAt, d.ErrorMessage);
}
