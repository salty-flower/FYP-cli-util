using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataCollection.Migrations
{
    /// <inheritdoc />
    public partial class AddPatternTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BugListDiscoveries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Conf = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    AnalysisJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BugListDiscoveries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IssueAnalyses",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    IssueNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AnalysisJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueAnalyses", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "KeywordRules",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    BaseConfidence = table.Column<double>(type: "REAL", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeywordRules", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PatternRules",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    BaseConfidence = table.Column<double>(type: "REAL", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternRules", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UrlTypeRules",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlTypeRules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_BugListDiscoveries_Conf_Year",
                table: "BugListDiscoveries",
                columns: new[] { "Conf", "Year" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_BugListDiscoveries_CreatedAt",
                table: "BugListDiscoveries",
                column: "CreatedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IssueAnalyses_CreatedAt",
                table: "IssueAnalyses",
                column: "CreatedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IssueAnalyses_Owner_Repository_IssueNumber",
                table: "IssueAnalyses",
                columns: new[] { "Owner", "Repository", "IssueNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IssueAnalyses_Status",
                table: "IssueAnalyses",
                column: "Status"
            );

            migrationBuilder.CreateIndex(
                name: "IX_KeywordRules_Category_IsActive",
                table: "KeywordRules",
                columns: new[] { "Category", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_KeywordRules_Priority",
                table: "KeywordRules",
                column: "Priority"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PatternRules_Category_IsActive",
                table: "PatternRules",
                columns: new[] { "Category", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PatternRules_Priority",
                table: "PatternRules",
                column: "Priority"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UrlTypeRules_Priority",
                table: "UrlTypeRules",
                column: "Priority"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UrlTypeRules_Type_IsActive",
                table: "UrlTypeRules",
                columns: new[] { "Type", "IsActive" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BugListDiscoveries");

            migrationBuilder.DropTable(name: "IssueAnalyses");

            migrationBuilder.DropTable(name: "KeywordRules");

            migrationBuilder.DropTable(name: "PatternRules");

            migrationBuilder.DropTable(name: "PdfData");

            migrationBuilder.DropTable(name: "UrlTypeRules");

            migrationBuilder.DropTable(name: "Papers");
        }
    }
}
