using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Users.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("users");

        mb.CreateTable(
            name: "user_profiles", schema: "users",
            columns: t => new
            {
                id                  = t.Column<Guid>(type: "uuid", nullable: false),
                user_id             = t.Column<Guid>(type: "uuid", nullable: false),
                first_name          = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                last_name           = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                email               = t.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                phone_number        = t.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                headline            = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                bio                 = t.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                location            = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                profile_picture_url = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                linkedin_url        = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                github_url          = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                website_url         = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_public           = t.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                years_of_experience = t.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                country             = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                time_zone           = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                is_deleted          = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                deleted_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                deleted_by          = t.Column<Guid>(type: "uuid", nullable: true),
                created_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at          = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_user_profiles", x => x.id));

        mb.CreateIndex("ix_user_profiles_user_id",    "user_profiles", "user_id",   "users", unique: true);
        mb.CreateIndex("ix_user_profiles_email",       "user_profiles", "email",     "users");
        mb.CreateIndex("ix_user_profiles_is_deleted",  "user_profiles", "is_deleted","users");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("user_profiles", "users");
        mb.Sql("DROP SCHEMA IF EXISTS users CASCADE;");
    }
}
