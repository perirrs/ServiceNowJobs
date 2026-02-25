using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SNHub.Matching.Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.EnsureSchema("matching");
        mb.CreateTable(
            name: "embedding_records", schema: "matching",
            columns: t => new
            {
                id            = t.Column<Guid>(type: "uuid", nullable: false),
                document_id   = t.Column<Guid>(type: "uuid", nullable: false),
                document_type = t.Column<int>(nullable: false),
                status        = t.Column<int>(nullable: false),
                error_message = t.Column<string>(maxLength: 2000, nullable: true),
                retry_count   = t.Column<int>(nullable: false, defaultValue: 0),
                last_indexed_at = t.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                created_at    = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at    = t.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_embedding_records", x => x.id));

        mb.CreateIndex("ix_embedding_records_document",   "embedding_records", ["document_id", "document_type"], "matching", unique: true);
        mb.CreateIndex("ix_embedding_records_status",     "embedding_records", "status",     "matching");
        mb.CreateIndex("ix_embedding_records_updated_at", "embedding_records", "updated_at", "matching");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("embedding_records", "matching");
        mb.Sql("DROP SCHEMA IF EXISTS matching CASCADE;");
    }
}
