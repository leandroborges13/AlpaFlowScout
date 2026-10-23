using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Penaltis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "disputa_importada",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "penaltis_cometidos",
                table: "estatisticas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penaltis_convertidos",
                table: "estatisticas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penaltis_defendidos",
                table: "estatisticas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penaltis_perdidos",
                table: "estatisticas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "penaltis_sofridos",
                table: "estatisticas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "cobrancas_disputa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    atleta_id = table.Column<int>(type: "integer", nullable: true),
                    jogador = table.Column<string>(type: "text", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    convertida = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_disputa", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_disputa_atleta_id",
                table: "cobrancas_disputa",
                column: "atleta_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_disputa_partida_id",
                table: "cobrancas_disputa",
                column: "partida_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobrancas_disputa");

            migrationBuilder.DropColumn(
                name: "disputa_importada",
                table: "partidas");

            migrationBuilder.DropColumn(
                name: "penaltis_cometidos",
                table: "estatisticas");

            migrationBuilder.DropColumn(
                name: "penaltis_convertidos",
                table: "estatisticas");

            migrationBuilder.DropColumn(
                name: "penaltis_defendidos",
                table: "estatisticas");

            migrationBuilder.DropColumn(
                name: "penaltis_perdidos",
                table: "estatisticas");

            migrationBuilder.DropColumn(
                name: "penaltis_sofridos",
                table: "estatisticas");
        }
    }
}
