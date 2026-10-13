using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class EstatisticasDosJogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "estatisticas_importadas",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "estatisticas_partida",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    posse = table.Column<int>(type: "integer", nullable: true),
                    finalizacoes = table.Column<int>(type: "integer", nullable: true),
                    finalizacoes_no_alvo = table.Column<int>(type: "integer", nullable: true),
                    finalizacoes_na_area = table.Column<int>(type: "integer", nullable: true),
                    escanteios = table.Column<int>(type: "integer", nullable: true),
                    faltas = table.Column<int>(type: "integer", nullable: true),
                    impedimentos = table.Column<int>(type: "integer", nullable: true),
                    amarelos = table.Column<int>(type: "integer", nullable: true),
                    vermelhos = table.Column<int>(type: "integer", nullable: true),
                    defesas = table.Column<int>(type: "integer", nullable: true),
                    passes = table.Column<int>(type: "integer", nullable: true),
                    passes_certos = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estatisticas_partida", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_estatisticas_partida_clube_id",
                table: "estatisticas_partida",
                column: "clube_id");

            migrationBuilder.CreateIndex(
                name: "ix_estatisticas_partida_partida_id_clube_id",
                table: "estatisticas_partida",
                columns: new[] { "partida_id", "clube_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estatisticas_partida");

            migrationBuilder.DropColumn(
                name: "estatisticas_importadas",
                table: "partidas");
        }
    }
}
