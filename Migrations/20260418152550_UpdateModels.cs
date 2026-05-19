using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBANK_ETUDIANT.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapitalActuel",
                table: "SavingsPockets");

            migrationBuilder.RenameColumn(
                name: "UserProfileId",
                table: "SavingsPockets",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "DateCible",
                table: "SavingsPockets",
                newName: "MontantActuel");

            migrationBuilder.RenameColumn(
                name: "CapitalInitial",
                table: "SavingsPockets",
                newName: "Cible");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsPockets_UserId",
                table: "SavingsPockets",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SavingsPockets_UserProfiles_UserId",
                table: "SavingsPockets",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SavingsPockets_UserProfiles_UserId",
                table: "SavingsPockets");

            migrationBuilder.DropIndex(
                name: "IX_SavingsPockets_UserId",
                table: "SavingsPockets");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "SavingsPockets",
                newName: "UserProfileId");

            migrationBuilder.RenameColumn(
                name: "MontantActuel",
                table: "SavingsPockets",
                newName: "DateCible");

            migrationBuilder.RenameColumn(
                name: "Cible",
                table: "SavingsPockets",
                newName: "CapitalInitial");

            migrationBuilder.AddColumn<decimal>(
                name: "CapitalActuel",
                table: "SavingsPockets",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
