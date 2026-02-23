using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Jobs.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "jobs");
        migrationBuilder.CreateTable(name: "jobs", schema: "jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                employer_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                requirements = table.Column<string>(type: "text", nullable: true),
                benefits = table.Column<string>(type: "text", nullable: true),
                company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                company_logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                job_type = table.Column<int>(type: "integer", nullable: false),
                work_mode = table.Column<int>(type: "integer", nullable: false),
                experience_level = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                salary_min = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                salary_max = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                salary_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true, defaultValue: "USD"),
                is_salary_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                skills_required = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                servicenow_versions = table.Column<string>(type: "jsonb", nullable: true),
                certifications_required = table.Column<string>(type: "jsonb", nullable: true),
                application_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_jobs", x => x.id));
        migrationBuilder.CreateIndex(name: "ix_jobs_employer", schema: "jobs", table: "jobs", column: "employer_id");
        migrationBuilder.CreateIndex(name: "ix_jobs_status", schema: "jobs", table: "jobs", column: "status");
        migrationBuilder.CreateIndex(name: "ix_jobs_status_created", schema: "jobs", table: "jobs", columns: ["status", "created_at"]);
        migrationBuilder.CreateIndex(name: "ix_jobs_country", schema: "jobs", table: "jobs", column: "country");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql("CREATE INDEX ix_jobs_title_trgm ON jobs.jobs USING gin (title gin_trgm_ops);");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "jobs", schema: "jobs");
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS jobs CASCADE;");
    }
}
