using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Auth.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "auth");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

        migrationBuilder.CreateTable(
            name: "users",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                profile_picture_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                is_email_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                is_suspended = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                suspension_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                suspended_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                email_verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                last_login_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                last_login_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                failed_login_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                locked_out_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                email_verification_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                email_verification_token_expiry = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                password_reset_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                password_reset_token_expiry = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                linkedin_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                google_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                azure_ad_object_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: "UTC"),
                country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                roles = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: "system"),
                updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: "system")
            },
            constraints: table => table.PrimaryKey("PK_users", x => x.id));

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false, defaultValue: ""),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                revoked_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                revoke_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                replaced_by_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.id);
                table.ForeignKey(name: "FK_refresh_tokens_users_user_id",
                    column: x => x.user_id, principalSchema: "auth", principalTable: "users",
                    principalColumn: "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "ix_users_email", schema: "auth", table: "users", column: "normalized_email", unique: true);
        migrationBuilder.CreateIndex(name: "ix_users_linkedin", schema: "auth", table: "users", column: "linkedin_id", unique: true, filter: "linkedin_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "ix_users_azure_ad", schema: "auth", table: "users", column: "azure_ad_object_id", unique: true, filter: "azure_ad_object_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "ix_users_active_created", schema: "auth", table: "users", columns: ["is_active", "created_at"]);
        migrationBuilder.CreateIndex(name: "ix_rt_token", schema: "auth", table: "refresh_tokens", column: "token", unique: true);
        migrationBuilder.CreateIndex(name: "ix_rt_user_expiry", schema: "auth", table: "refresh_tokens", columns: ["user_id", "expires_at"]);
        migrationBuilder.CreateIndex(name: "ix_rt_active", schema: "auth", table: "refresh_tokens", column: "token", filter: "revoked_at IS NULL");
        migrationBuilder.Sql("CREATE INDEX ix_users_email_trgm ON auth.users USING gin (email gin_trgm_ops);");
        migrationBuilder.Sql("CREATE INDEX ix_users_name_trgm ON auth.users USING gin ((first_name || ' ' || last_name) gin_trgm_ops);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "refresh_tokens", schema: "auth");
        migrationBuilder.DropTable(name: "users", schema: "auth");
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS auth CASCADE;");
    }
}
