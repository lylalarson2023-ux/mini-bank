using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class AddEpargneAndInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DerniereMajInterets",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "SoldeEpargne",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DerniereMajInterets",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SoldeEpargne",
                table: "UserProfiles");
        }
    }
}
