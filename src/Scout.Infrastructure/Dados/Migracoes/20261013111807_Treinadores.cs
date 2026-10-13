using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Treinadores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "treinadores_importados",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "treinadores",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    foto = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treinadores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "treinadores_na_partida",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    treinador_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treinadores_na_partida", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_treinadores_na_partida_partida_id_clube_id",
                table: "treinadores_na_partida",
                columns: new[] { "partida_id", "clube_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treinadores_na_partida_treinador_id",
                table: "treinadores_na_partida",
                column: "treinador_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "treinadores");

            migrationBuilder.DropTable(
                name: "treinadores_na_partida");

            migrationBuilder.DropColumn(
                name: "treinadores_importados",
                table: "partidas");
        }
    }
}
