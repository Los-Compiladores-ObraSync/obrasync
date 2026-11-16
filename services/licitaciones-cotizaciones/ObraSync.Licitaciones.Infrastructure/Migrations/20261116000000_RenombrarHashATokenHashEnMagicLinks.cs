using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenombrarHashATokenHashEnMagicLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MagicLinks_Hash",
                table: "MagicLinks");

            migrationBuilder.RenameColumn(
                name: "Hash",
                table: "MagicLinks",
                newName: "TokenHash");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "MagicLinks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_TokenHash",
                table: "MagicLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MagicLinks_TokenHash",
                table: "MagicLinks");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "MagicLinks",
                newName: "Hash");

            migrationBuilder.AlterColumn<string>(
                name: "Hash",
                table: "MagicLinks",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_Hash",
                table: "MagicLinks",
                column: "Hash",
                unique: true);
        }
    }
}
