using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.JobEnhancer.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("enhancer");
        mb.CreateTable(
            name: "enhancement_results", schema: "enhancer",
            columns: t => new
            {
                id                   = t.Column<Guid>(type: "uuid", nullable: false),
                job_id               = t.Column<Guid>(type: "uuid", nullable: false),
                requested_by         = t.Column<Guid>(type: "uuid", nullable: false),
                status               = t.Column<int>(nullable: false),
                error_message        = t.Column<string>(maxLength: 2000, nullable: true),
                original_title       = t.Column<string>(maxLength: 200, nullable: false),
                original_description = t.Column<string>(nullable: false),
                original_requirements= t.Column<string>(nullable: true),
                enhanced_title       = t.Column<string>(maxLength: 200, nullable: true),
                enhanced_description = t.Column<string>(nullable: true),
                enhanced_requirements= t.Column<string>(nullable: true),
                score_before         = t.Column<int>(nullable: false, defaultValue: 0),
                score_after          = t.Column<int>(nullable: false, defaultValue: 0),
                bias_issues_json     = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                missing_fields_json  = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                improvements_json    = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                suggested_skills_json= t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                is_accepted          = t.Column<bool>(nullable: false, defaultValue: false),
                accepted_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                created_at           = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at           = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_enhancement_results", x => x.id));

        mb.CreateIndex("ix_enhancement_results_job_id",      "enhancement_results", "job_id",       "enhancer");
        mb.CreateIndex("ix_enhancement_results_requested_by","enhancement_results", "requested_by", "enhancer");
        mb.CreateIndex("ix_enhancement_results_created_at",  "enhancement_results", "created_at",   "enhancer");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("enhancement_results", "enhancer");
        mb.Sql("DROP SCHEMA IF EXISTS enhancer CASCADE;");
    }
}
