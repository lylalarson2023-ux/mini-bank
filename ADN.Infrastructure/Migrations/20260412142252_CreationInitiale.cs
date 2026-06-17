using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class CreationInitiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocEtudiantUrl",
                table: "UserProfiles");

            migrationBuilder.RenameColumn(
                name: "SituationFamiliale",
                table: "UserProfiles",
                newName: "DateInscription");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DateInscription",
                table: "UserProfiles",
                newName: "SituationFamiliale");

            migrationBuilder.AddColumn<string>(
                name: "DocEtudiantUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
