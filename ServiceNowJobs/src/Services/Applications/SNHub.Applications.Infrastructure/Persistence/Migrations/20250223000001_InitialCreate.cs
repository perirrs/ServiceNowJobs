using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Applications.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("applications");

        mb.CreateTable(
            name: "applications",
            schema: "applications",
            columns: t => new
            {
                id               = t.Column<Guid>(type: "uuid", nullable: false),
                job_id           = t.Column<Guid>(type: "uuid", nullable: false),
                candidate_id     = t.Column<Guid>(type: "uuid", nullable: false),
                status           = t.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                cover_letter     = t.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                cv_url           = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                employer_notes   = t.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                rejection_reason = t.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                applied_at       = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at       = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                status_changed_at = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_applications", x => x.id));

        mb.CreateIndex(name: "ix_applications_job_candidate", schema: "applications", table: "applications",
            columns: new[] { "job_id", "candidate_id" }, unique: true);
        mb.CreateIndex(name: "ix_applications_candidate", schema: "applications", table: "applications", column: "candidate_id");
        mb.CreateIndex(name: "ix_applications_job", schema: "applications", table: "applications", column: "job_id");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "applications", schema: "applications");
        mb.Sql("DROP SCHEMA IF EXISTS applications CASCADE;");
    }
}
