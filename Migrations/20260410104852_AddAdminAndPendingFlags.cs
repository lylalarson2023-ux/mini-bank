using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAndPendingFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateLimiteRemboursement",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SoldeEpargne",
                table: "UserProfiles");

            migrationBuilder.RenameColumn(
                name: "Montant",
                table: "Transactions",
                newName: "MontantBrut");

            migrationBuilder.AlterColumn<decimal>(
                name: "Solde",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "Dette",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PendingPremiumUpgrade",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Frais",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SavingsPockets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Objectif = table.Column<string>(type: "TEXT", nullable: false),
                    CapitalInitial = table.Column<decimal>(type: "TEXT", nullable: false),
                    CapitalActuel = table.Column<decimal>(type: "TEXT", nullable: false),
                    DateCible = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StatutGoal = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsPockets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavingsPockets");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PendingPremiumUpgrade",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Frais",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "MontantBrut",
                table: "Transactions",
                newName: "Montant");

            migrationBuilder.AlterColumn<double>(
                name: "Solde",
                table: "UserProfiles",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "Dette",
                table: "UserProfiles",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateLimiteRemboursement",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SoldeEpargne",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
