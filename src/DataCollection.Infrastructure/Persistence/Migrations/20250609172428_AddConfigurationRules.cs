using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataCollection.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationRules",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRules_Category_IsActive",
                table: "ConfigurationRules",
                columns: new[] { "Category", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRules_Category_Key",
                table: "ConfigurationRules",
                columns: new[] { "Category", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRules_Priority",
                table: "ConfigurationRules",
                column: "Priority"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConfigurationRules");
        }
    }
}
