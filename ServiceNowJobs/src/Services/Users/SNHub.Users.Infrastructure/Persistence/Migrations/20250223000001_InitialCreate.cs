using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Users.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "users");
        migrationBuilder.CreateTable(
            name: "user_profiles", schema: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                profile_picture_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                cv_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                linkedin_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                github_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                website_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                years_of_experience = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_user_profiles", x => x.id));
        migrationBuilder.CreateIndex(name: "ix_user_profiles_user_id", schema: "users", table: "user_profiles", column: "user_id", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_profiles", schema: "users");
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS users CASCADE;");
    }
}
