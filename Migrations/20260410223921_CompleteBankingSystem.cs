using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBANK_ETUDIANT.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBankingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdresseCasablanca",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocDomicileUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocEtudiantUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocIdentiteUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocPhotoUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LieuNaissance",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NiveauEtude",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PassportOuCIN",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PendingCreditAmount",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PendingCreditMotif",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PendingCreditRequest",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdresseCasablanca",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocDomicileUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocEtudiantUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocIdentiteUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocPhotoUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LieuNaissance",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "NiveauEtude",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PassportOuCIN",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PendingCreditAmount",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PendingCreditMotif",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PendingCreditRequest",
                table: "UserProfiles");
        }
    }
}
