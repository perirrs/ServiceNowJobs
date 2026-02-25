using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.CvParser.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("cvparser");

        mb.CreateTable(
            name: "cv_parse_results", schema: "cvparser",
            columns: t => new
            {
                id                      = t.Column<Guid>(type: "uuid", nullable: false),
                user_id                 = t.Column<Guid>(type: "uuid", nullable: false),
                blob_path               = t.Column<string>(maxLength: 1024, nullable: false),
                original_file_name      = t.Column<string>(maxLength: 255, nullable: false),
                content_type            = t.Column<string>(maxLength: 100, nullable: false),
                file_size_bytes         = t.Column<long>(nullable: false),
                status                  = t.Column<int>(nullable: false),
                error_message           = t.Column<string>(maxLength: 2000, nullable: true),
                first_name              = t.Column<string>(maxLength: 100, nullable: true),
                last_name               = t.Column<string>(maxLength: 100, nullable: true),
                email                   = t.Column<string>(maxLength: 320, nullable: true),
                phone                   = t.Column<string>(maxLength: 50, nullable: true),
                location                = t.Column<string>(maxLength: 200, nullable: true),
                headline                = t.Column<string>(maxLength: 200, nullable: true),
                summary                 = t.Column<string>(maxLength: 2000, nullable: true),
                current_role            = t.Column<string>(maxLength: 200, nullable: true),
                years_of_experience     = t.Column<int>(nullable: true),
                linkedin_url            = t.Column<string>(maxLength: 500, nullable: true),
                github_url              = t.Column<string>(maxLength: 500, nullable: true),
                skills_json             = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                certifications_json     = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                servicenow_versions_json= t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                overall_confidence      = t.Column<int>(nullable: false, defaultValue: 0),
                field_confidences_json  = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                is_applied              = t.Column<bool>(nullable: false, defaultValue: false),
                applied_at              = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                created_at              = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at              = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_cv_parse_results", x => x.id));

        mb.CreateIndex("ix_cv_parse_results_user_id",   "cv_parse_results", "user_id",   "cvparser");
        mb.CreateIndex("ix_cv_parse_results_status",    "cv_parse_results", "status",    "cvparser");
        mb.CreateIndex("ix_cv_parse_results_created_at","cv_parse_results", "created_at","cvparser");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("cv_parse_results", "cvparser");
        mb.Sql("DROP SCHEMA IF EXISTS cvparser CASCADE;");
    }
}
