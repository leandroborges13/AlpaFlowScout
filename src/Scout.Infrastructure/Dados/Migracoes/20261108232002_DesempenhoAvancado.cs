using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class DesempenhoAvancado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pitch_id",
                table: "partidas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pitch_importada",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "desempenhos_avancados",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    atleta_id = table.Column<int>(type: "integer", nullable: true),
                    pitch_jogador_id = table.Column<string>(type: "text", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    minutos = table.Column<int>(type: "integer", nullable: false),
                    nota = table.Column<double>(type: "double precision", nullable: true),
                    gols = table.Column<int>(type: "integer", nullable: false),
                    assistencias = table.Column<int>(type: "integer", nullable: false),
                    xg = table.Column<double>(type: "double precision", nullable: true),
                    xa = table.Column<double>(type: "double precision", nullable: true),
                    finalizacoes = table.Column<int>(type: "integer", nullable: false),
                    finalizacoes_no_alvo = table.Column<int>(type: "integer", nullable: false),
                    toques = table.Column<int>(type: "integer", nullable: false),
                    toques_na_area = table.Column<int>(type: "integer", nullable: false),
                    passes = table.Column<int>(type: "integer", nullable: false),
                    passes_certos = table.Column<int>(type: "integer", nullable: false),
                    passes_terco_final = table.Column<int>(type: "integer", nullable: false),
                    passes_progressivos = table.Column<int>(type: "integer", nullable: false),
                    passes_na_area = table.Column<int>(type: "integer", nullable: false),
                    bolas_longas = table.Column<int>(type: "integer", nullable: false),
                    bolas_longas_certas = table.Column<int>(type: "integer", nullable: false),
                    cruzamentos = table.Column<int>(type: "integer", nullable: false),
                    cruzamentos_certos = table.Column<int>(type: "integer", nullable: false),
                    chances_criadas = table.Column<int>(type: "integer", nullable: false),
                    conducoes_progressivas = table.Column<int>(type: "integer", nullable: false),
                    conducoes_terco_final = table.Column<int>(type: "integer", nullable: false),
                    dribles = table.Column<int>(type: "integer", nullable: false),
                    dribles_certos = table.Column<int>(type: "integer", nullable: false),
                    desarmes = table.Column<int>(type: "integer", nullable: false),
                    interceptacoes = table.Column<int>(type: "integer", nullable: false),
                    recuperacoes = table.Column<int>(type: "integer", nullable: false),
                    cortes = table.Column<int>(type: "integer", nullable: false),
                    duelos_ganhos = table.Column<int>(type: "integer", nullable: false),
                    duelos_perdidos = table.Column<int>(type: "integer", nullable: false),
                    aereos = table.Column<int>(type: "integer", nullable: false),
                    aereos_ganhos = table.Column<int>(type: "integer", nullable: false),
                    faltas_cometidas = table.Column<int>(type: "integer", nullable: false),
                    faltas_sofridas = table.Column<int>(type: "integer", nullable: false),
                    perdas_de_posse = table.Column<int>(type: "integer", nullable: false),
                    defesas = table.Column<int>(type: "integer", nullable: false),
                    gols_evitados = table.Column<double>(type: "double precision", nullable: true),
                    sca = table.Column<int>(type: "integer", nullable: false),
                    gca = table.Column<int>(type: "integer", nullable: false),
                    xg_chain = table.Column<double>(type: "double precision", nullable: true),
                    xt = table.Column<double>(type: "double precision", nullable: true),
                    vaep = table.Column<double>(type: "double precision", nullable: true),
                    bruto = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desempenhos_avancados", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_desempenhos_avancados_atleta_id",
                table: "desempenhos_avancados",
                column: "atleta_id");

            migrationBuilder.CreateIndex(
                name: "ix_desempenhos_avancados_partida_id_pitch_jogador_id",
                table: "desempenhos_avancados",
                columns: new[] { "partida_id", "pitch_jogador_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desempenhos_avancados");

            migrationBuilder.DropColumn(
                name: "pitch_id",
                table: "partidas");

            migrationBuilder.DropColumn(
                name: "pitch_importada",
                table: "partidas");
        }
    }
}
