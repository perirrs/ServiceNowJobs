using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Applications.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("applications");
        mb.CreateTable("applications", "applications", t => new
        {
            id               = t.Column<Guid>("uuid"),
            job_id           = t.Column<Guid>("uuid"),
            candidate_id     = t.Column<Guid>("uuid"),
            status           = t.Column<int>("integer", defaultValue: 1),
            cover_letter     = t.Column<string>("character varying(5000)", maxLength: 5000, nullable: true),
            cv_url           = t.Column<string>("character varying(2048)", maxLength: 2048, nullable: true),
            employer_notes   = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
            rejection_reason = t.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            applied_at       = t.Column<DateTimeOffset>("timestamptz"),
            updated_at       = t.Column<DateTimeOffset>("timestamptz"),
            status_changed_at = t.Column<DateTimeOffset>("timestamptz", nullable: true)
        }, c => c.PrimaryKey("PK_applications", x => x.id));

        mb.CreateIndex("ix_applications_job_candidate", "applications", "applications",
            new[] { "job_id", "candidate_id" }, unique: true);
        mb.CreateIndex("ix_applications_candidate", "applications", "applications", "candidate_id");
        mb.CreateIndex("ix_applications_job", "applications", "applications", "job_id");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("applications", "applications");
        mb.Sql("DROP SCHEMA IF EXISTS applications CASCADE;");
    }
}
