using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Profiles.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("profiles");

        mb.CreateTable(
            name: "candidate_profiles",
            schema: "profiles",
            columns: t => new
            {
                id                  = t.Column<Guid>(type: "uuid", nullable: false),
                user_id             = t.Column<Guid>(type: "uuid", nullable: false),
                headline            = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                bio                 = t.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                experience_level    = t.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                years_of_experience = t.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                availability        = t.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                current_role        = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                desired_role        = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                location            = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                country             = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                time_zone           = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                profile_picture_url = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                cv_url              = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                linkedin_url        = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                github_url          = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                website_url         = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_public           = t.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                desired_salary_min  = t.Column<decimal>(type: "decimal(12,2)", nullable: true),
                desired_salary_max  = t.Column<decimal>(type: "decimal(12,2)", nullable: true),
                salary_currency     = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true, defaultValue: "USD"),
                open_to_remote      = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                open_to_relocation  = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                skills              = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                certifications      = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                servicenow_versions = t.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                profile_completeness = t.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_candidate_profiles", x => x.id));

        mb.CreateIndex(name: "ix_candidate_profiles_user", schema: "profiles", table: "candidate_profiles",
            column: "user_id", unique: true);

        mb.CreateTable(
            name: "employer_profiles",
            schema: "profiles",
            columns: t => new
            {
                id                    = t.Column<Guid>(type: "uuid", nullable: false),
                user_id               = t.Column<Guid>(type: "uuid", nullable: false),
                company_name          = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                company_description   = t.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                industry              = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                company_size          = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                headquarters_city     = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                headquarters_country  = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                website_url           = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                linkedin_url          = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                logo_url              = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                is_verified           = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at            = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at            = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_employer_profiles", x => x.id));

        mb.CreateIndex(name: "ix_employer_profiles_user", schema: "profiles", table: "employer_profiles",
            column: "user_id", unique: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "candidate_profiles", schema: "profiles");
        mb.DropTable(name: "employer_profiles", schema: "profiles");
        mb.Sql("DROP SCHEMA IF EXISTS profiles CASCADE;");
    }
}
