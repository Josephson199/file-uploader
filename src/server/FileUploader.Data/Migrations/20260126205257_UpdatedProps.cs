using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileUploader.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedProps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "Uploads");

            migrationBuilder.RenameColumn(
                name: "Sid",
                table: "Users",
                newName: "Sub");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Sid",
                table: "Users",
                newName: "IX_Users_Sub");

            migrationBuilder.AddColumn<string>(
                name: "FileKey",
                table: "Uploads",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileKey",
                table: "Uploads");

            migrationBuilder.RenameColumn(
                name: "Sub",
                table: "Users",
                newName: "Sid");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Sub",
                table: "Users",
                newName: "IX_Users_Sid");

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "Uploads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
