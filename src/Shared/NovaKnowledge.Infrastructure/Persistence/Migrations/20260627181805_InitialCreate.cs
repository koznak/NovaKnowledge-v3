using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    BlobPath = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Department = table.Column<string>(type: "text", nullable: true),
                    DocType = table.Column<string>(type: "text", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ChunkingStrategy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DatasetPath = table.Column<string>(type: "text", nullable: true),
                    FaithfulnessScore = table.Column<double>(type: "double precision", precision: 5, scale: 4, nullable: true),
                    AnswerRelevanceScore = table.Column<double>(type: "double precision", precision: 5, scale: 4, nullable: true),
                    ContextPrecisionScore = table.Column<double>(type: "double precision", precision: 5, scale: 4, nullable: true),
                    ContextRecallScore = table.Column<double>(type: "double precision", precision: 5, scale: 4, nullable: true),
                    ReportBlobPath = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Feedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    UserPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AbTestTag = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    DefaultChunkingStrategy = table.Column<int>(type: "integer", nullable: false),
                    FixedChunkSize = table.Column<int>(type: "integer", nullable: false),
                    FixedChunkOverlap = table.Column<int>(type: "integer", nullable: false),
                    RetrievalTopK = table.Column<int>(type: "integer", nullable: false),
                    ChatModel = table.Column<string>(type: "text", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "text", nullable: false),
                    UseMultiAgent = table.Column<bool>(type: "boolean", nullable: false),
                    DailyTokenBudget = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Input = table.Column<string>(type: "text", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuns_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CitationsJson = table.Column<string>(type: "text", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    RetrievalLatencyMs = table.Column<int>(type: "integer", nullable: false),
                    LlmLatencyMs = table.Column<int>(type: "integer", nullable: false),
                    TotalLatencyMs = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SearchIndexKey = table.Column<string>(type: "text", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    StrategyUsed = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_ConversationId",
                table: "AgentRuns",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_ContentHash",
                table: "Documents",
                columns: new[] { "TenantId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_TenantId_Name_Version",
                table: "PromptTemplates",
                columns: new[] { "TenantId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_TenantId",
                table: "TenantSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "EvaluationRuns");

            migrationBuilder.DropTable(
                name: "Feedbacks");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropTable(
                name: "TenantSettings");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Conversations");
        }
    }
}
