using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace SNHub.Notifications.Infrastructure.Persistence.Migrations;
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("notifications");
        mb.CreateTable("notifications", "notifications", t => new
        {
            id = t.Column<Guid>("uuid"), user_id = t.Column<Guid>("uuid"),
            type = t.Column<int>("integer"),
            title = t.Column<string>("character varying(200)"),
            message = t.Column<string>("character varying(1000)"),
            action_url = t.Column<string>("character varying(2048)", nullable: true),
            metadata = t.Column<string>("jsonb", nullable: true),
            is_read = t.Column<bool>("boolean", defaultValue: false),
            created_at = t.Column<DateTimeOffset>("timestamptz"),
            read_at = t.Column<DateTimeOffset>("timestamptz", nullable: true)
        }, c => c.PrimaryKey("PK_notifications", x => x.id));
        mb.CreateIndex("ix_notifications_user_read", "notifications", "notifications", new[] { "user_id", "is_read" });
        mb.CreateIndex("ix_notifications_created", "notifications", "notifications", "created_at");
    }
    protected override void Down(MigrationBuilder mb) { mb.DropTable("notifications", "notifications"); mb.Sql("DROP SCHEMA IF EXISTS notifications CASCADE;"); }
}
