using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Notifications.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("notifications");

        mb.CreateTable(
            name: "notifications",
            schema: "notifications",
            columns: t => new
            {
                id         = t.Column<Guid>(type: "uuid", nullable: false),
                user_id    = t.Column<Guid>(type: "uuid", nullable: false),
                type       = t.Column<int>(type: "integer", nullable: false),
                title      = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                message    = t.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                action_url = t.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                metadata   = t.Column<string>(type: "jsonb", nullable: true),
                is_read    = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                read_at    = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_notifications", x => x.id));

        mb.CreateIndex(name: "ix_notifications_user_read", schema: "notifications", table: "notifications",
            columns: new[] { "user_id", "is_read" });
        mb.CreateIndex(name: "ix_notifications_created", schema: "notifications", table: "notifications",
            column: "created_at");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "notifications", schema: "notifications");
        mb.Sql("DROP SCHEMA IF EXISTS notifications CASCADE;");
    }
}
