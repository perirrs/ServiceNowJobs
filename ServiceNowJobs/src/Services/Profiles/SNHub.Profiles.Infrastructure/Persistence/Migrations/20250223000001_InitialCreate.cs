using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace SNHub.Profiles.Infrastructure.Persistence.Migrations;
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("profiles");
        mb.CreateTable("candidate_profiles", "profiles", t => new
        {
            id = t.Column<Guid>("uuid"), user_id = t.Column<Guid>("uuid"),
            headline = t.Column<string>("character varying(200)", nullable: true),
            bio = t.Column<string>("character varying(3000)", nullable: true),
            experience_level = t.Column<int>("integer", defaultValue: 3),
            years_of_experience = t.Column<int>("integer", defaultValue: 0),
            availability = t.Column<int>("integer", defaultValue: 2),
            current_role = t.Column<string>("character varying(200)", nullable: true),
            desired_role = t.Column<string>("character varying(200)", nullable: true),
            location = t.Column<string>("character varying(200)", nullable: true),
            country = t.Column<string>("character varying(3)", nullable: true),
            time_zone = t.Column<string>("character varying(100)", nullable: true),
            profile_picture_url = t.Column<string>("character varying(2048)", nullable: true),
            cv_url = t.Column<string>("character varying(2048)", nullable: true),
            linkedin_url = t.Column<string>("character varying(500)", nullable: true),
            github_url = t.Column<string>("character varying(500)", nullable: true),
            website_url = t.Column<string>("character varying(500)", nullable: true),
            is_public = t.Column<bool>("boolean", defaultValue: true),
            desired_salary_min = t.Column<decimal>("decimal(12,2)", nullable: true),
            desired_salary_max = t.Column<decimal>("decimal(12,2)", nullable: true),
            salary_currency = t.Column<string>("character varying(3)", defaultValue: "USD", nullable: true),
            open_to_remote = t.Column<bool>("boolean", defaultValue: false),
            open_to_relocation = t.Column<bool>("boolean", defaultValue: false),
            skills = t.Column<string>("jsonb", defaultValue: "[]"),
            certifications = t.Column<string>("jsonb", defaultValue: "[]"),
            servicenow_versions = t.Column<string>("jsonb", defaultValue: "[]"),
            profile_completeness = t.Column<int>("integer", defaultValue: 0),
            created_at = t.Column<DateTimeOffset>("timestamptz"),
            updated_at = t.Column<DateTimeOffset>("timestamptz")
        }, c => c.PrimaryKey("PK_candidate_profiles", x => x.id));
        mb.CreateIndex("ix_candidate_profiles_user", "candidate_profiles", "profiles", "user_id", unique: true);

        mb.CreateTable("employer_profiles", "profiles", t => new
        {
            id = t.Column<Guid>("uuid"), user_id = t.Column<Guid>("uuid"),
            company_name = t.Column<string>("character varying(200)", nullable: true),
            company_description = t.Column<string>("character varying(5000)", nullable: true),
            industry = t.Column<string>("character varying(100)", nullable: true),
            company_size = t.Column<string>("character varying(20)", nullable: true),
            headquarters_city = t.Column<string>("character varying(100)", nullable: true),
            headquarters_country = t.Column<string>("character varying(3)", nullable: true),
            website_url = t.Column<string>("character varying(500)", nullable: true),
            linkedin_url = t.Column<string>("character varying(500)", nullable: true),
            logo_url = t.Column<string>("character varying(2048)", nullable: true),
            is_verified = t.Column<bool>("boolean", defaultValue: false),
            created_at = t.Column<DateTimeOffset>("timestamptz"),
            updated_at = t.Column<DateTimeOffset>("timestamptz")
        }, c => c.PrimaryKey("PK_employer_profiles", x => x.id));
        mb.CreateIndex("ix_employer_profiles_user", "employer_profiles", "profiles", "user_id", unique: true);
    }
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("candidate_profiles", "profiles");
        mb.DropTable("employer_profiles", "profiles");
        mb.Sql("DROP SCHEMA IF EXISTS profiles CASCADE;");
    }
}
