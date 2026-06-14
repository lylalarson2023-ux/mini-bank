using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MontantBrut",
                table: "Transactions",
                newName: "Montant");

            migrationBuilder.AddColumn<string>(
                name: "AnneeEtude",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CodePostal",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CvUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocBourseUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocProUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DocScolariteUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Etablissement",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Filiere",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Genre",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MatriculeEtudiant",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SituationFamiliale",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StatutEtudiant",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Ville",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Libelle",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserProfileId",
                table: "SavingsPockets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnneeEtude",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "CodePostal",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "CvUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocBourseUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocProUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DocScolariteUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Etablissement",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Filiere",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Genre",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "MatriculeEtudiant",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SituationFamiliale",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "StatutEtudiant",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Ville",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Libelle",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "SavingsPockets");

            migrationBuilder.RenameColumn(
                name: "Montant",
                table: "Transactions",
                newName: "MontantBrut");
        }
    }
}
