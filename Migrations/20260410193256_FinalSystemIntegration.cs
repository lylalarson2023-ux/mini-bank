using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class FinalSystemIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CguAcceptees",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateNaissance",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationalite",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Prenom",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReseauPrincipal",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SituationMatrimoniale",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telephone",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AdminLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Cible = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    StatutResultat = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminLogs");

            migrationBuilder.DropColumn(
                name: "CguAcceptees",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DateNaissance",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Nationalite",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Prenom",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReseauPrincipal",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SituationMatrimoniale",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Telephone",
                table: "UserProfiles");
        }
    }
}
