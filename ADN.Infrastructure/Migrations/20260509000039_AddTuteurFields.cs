using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADN_pay.Migrations
{
    /// <inheritdoc />
    public partial class AddTuteurFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TuteurAutorise",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TuteurEmail",
                table: "UserProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TuteurAutorise",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "TuteurEmail",
                table: "UserProfiles");
        }
    }
}
