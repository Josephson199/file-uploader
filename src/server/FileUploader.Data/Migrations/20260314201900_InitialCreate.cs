using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FileUploader.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sub = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    JobId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_Jobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UploadCandidates",
                columns: table => new
                {
                    UploadCandidateId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    ObjectFileKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadCandidates", x => x.UploadCandidateId);
                    table.ForeignKey(
                        name: "FK_UploadCandidates_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Uploads",
                columns: table => new
                {
                    UploadId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OrignalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TempObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PersistedObjectKey = table.Column<string>(type: "text", nullable: true),
                    FileId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Uploads", x => x.UploadId);
                    table.ForeignKey(
                        name: "FK_Uploads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadValidationResults",
                columns: table => new
                {
                    UploadValidationErrorId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UploadId = table.Column<int>(type: "integer", nullable: false),
                    ValidationErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValidationType = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadValidationResults", x => x.UploadValidationErrorId);
                    table.ForeignKey(
                        name: "FK_UploadValidationResults_Uploads_UploadId",
                        column: x => x.UploadId,
                        principalTable: "Uploads",
                        principalColumn: "UploadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadVirusScanResults",
                columns: table => new
                {
                    UploadVirusScanResultId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UploadId = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    RawResult = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadVirusScanResults", x => x.UploadVirusScanResultId);
                    table.ForeignKey(
                        name: "FK_UploadVirusScanResults_Uploads_UploadId",
                        column: x => x.UploadId,
                        principalTable: "Uploads",
                        principalColumn: "UploadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InfectedFile",
                columns: table => new
                {
                    InfectedFileId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UploadVirusScanResultId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VirusName = table.Column<string>(type: "character varying(1048)", maxLength: 1048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfectedFile", x => x.InfectedFileId);
                    table.ForeignKey(
                        name: "FK_InfectedFile_UploadVirusScanResults_UploadVirusScanResultId",
                        column: x => x.UploadVirusScanResultId,
                        principalTable: "UploadVirusScanResults",
                        principalColumn: "UploadVirusScanResultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InfectedFile_UploadVirusScanResultId",
                table: "InfectedFile",
                column: "UploadVirusScanResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_UserId",
                table: "Jobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadCandidates_FileId",
                table: "UploadCandidates",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadCandidates_OwnerUserId",
                table: "UploadCandidates",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Uploads_FileId",
                table: "Uploads",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Uploads_UserId",
                table: "Uploads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadValidationResults_UploadId",
                table: "UploadValidationResults",
                column: "UploadId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadVirusScanResults_UploadId",
                table: "UploadVirusScanResults",
                column: "UploadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Sub",
                table: "Users",
                column: "Sub",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InfectedFile");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "UploadCandidates");

            migrationBuilder.DropTable(
                name: "UploadValidationResults");

            migrationBuilder.DropTable(
                name: "UploadVirusScanResults");

            migrationBuilder.DropTable(
                name: "Uploads");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
