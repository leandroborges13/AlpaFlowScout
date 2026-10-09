using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "atletas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    nome_completo = table.Column<string>(type: "text", nullable: true),
                    nascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    nacionalidade = table.Column<string>(type: "text", nullable: true),
                    altura_cm = table.Column<int>(type: "integer", nullable: true),
                    peso_kg = table.Column<int>(type: "integer", nullable: true),
                    foto = table.Column<string>(type: "text", nullable: true),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_atletas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clubes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    escudo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clubes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competicoes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    pais = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competicoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consultas_diarias",
                columns: table => new
                {
                    dia = table.Column<DateOnly>(type: "date", nullable: false),
                    usadas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultas_diarias", x => x.dia);
                });

            migrationBuilder.CreateTable(
                name: "escalacoes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    atleta_id = table.Column<int>(type: "integer", nullable: false),
                    esquema = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<string>(type: "text", nullable: true),
                    setor = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    funcao = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escalacoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partidas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    competicao_id = table.Column<int>(type: "integer", nullable: false),
                    temporada = table.Column<int>(type: "integer", nullable: false),
                    rodada = table.Column<string>(type: "text", nullable: true),
                    data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    mandante_id = table.Column<int>(type: "integer", nullable: false),
                    visitante_id = table.Column<int>(type: "integer", nullable: false),
                    gols_mandante = table.Column<int>(type: "integer", nullable: true),
                    gols_visitante = table.Column<int>(type: "integer", nullable: true),
                    escalacoes_importadas = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partidas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "respostas_provedor",
                columns: table => new
                {
                    chave = table.Column<string>(type: "text", nullable: false),
                    json = table.Column<string>(type: "jsonb", nullable: false),
                    obtida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_respostas_provedor", x => x.chave);
                });

            migrationBuilder.CreateTable(
                name: "estatisticas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    atleta_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    competicao_id = table.Column<int>(type: "integer", nullable: false),
                    temporada = table.Column<int>(type: "integer", nullable: false),
                    posicao_provedor = table.Column<string>(type: "text", nullable: true),
                    jogos = table.Column<int>(type: "integer", nullable: false),
                    titular = table.Column<int>(type: "integer", nullable: false),
                    minutos = table.Column<int>(type: "integer", nullable: false),
                    gols = table.Column<int>(type: "integer", nullable: false),
                    assistencias = table.Column<int>(type: "integer", nullable: false),
                    gols_sofridos = table.Column<int>(type: "integer", nullable: false),
                    defesas = table.Column<int>(type: "integer", nullable: false),
                    finalizacoes = table.Column<int>(type: "integer", nullable: false),
                    finalizacoes_no_alvo = table.Column<int>(type: "integer", nullable: false),
                    passes = table.Column<int>(type: "integer", nullable: false),
                    passes_decisivos = table.Column<int>(type: "integer", nullable: false),
                    precisao_passes = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    desarmes = table.Column<int>(type: "integer", nullable: false),
                    bloqueios = table.Column<int>(type: "integer", nullable: false),
                    interceptacoes = table.Column<int>(type: "integer", nullable: false),
                    duelos = table.Column<int>(type: "integer", nullable: false),
                    duelos_ganhos = table.Column<int>(type: "integer", nullable: false),
                    dribles_tentados = table.Column<int>(type: "integer", nullable: false),
                    dribles_certos = table.Column<int>(type: "integer", nullable: false),
                    faltas_cometidas = table.Column<int>(type: "integer", nullable: false),
                    faltas_sofridas = table.Column<int>(type: "integer", nullable: false),
                    amarelos = table.Column<int>(type: "integer", nullable: false),
                    vermelhos = table.Column<int>(type: "integer", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estatisticas", x => x.id);
                    table.ForeignKey(
                        name: "fk_estatisticas_atletas_atleta_id",
                        column: x => x.atleta_id,
                        principalTable: "atletas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_estatisticas_clubes_clube_id",
                        column: x => x.clube_id,
                        principalTable: "clubes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_estatisticas_competicoes_competicao_id",
                        column: x => x.competicao_id,
                        principalTable: "competicoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_atletas_nome",
                table: "atletas",
                column: "nome");

            migrationBuilder.CreateIndex(
                name: "ix_escalacoes_atleta_id",
                table: "escalacoes",
                column: "atleta_id");

            migrationBuilder.CreateIndex(
                name: "ix_escalacoes_partida_id_atleta_id",
                table: "escalacoes",
                columns: new[] { "partida_id", "atleta_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_estatisticas_atleta_id_clube_id_competicao_id_temporada",
                table: "estatisticas",
                columns: new[] { "atleta_id", "clube_id", "competicao_id", "temporada" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_estatisticas_clube_id",
                table: "estatisticas",
                column: "clube_id");

            migrationBuilder.CreateIndex(
                name: "ix_estatisticas_competicao_id_temporada",
                table: "estatisticas",
                columns: new[] { "competicao_id", "temporada" });

            migrationBuilder.CreateIndex(
                name: "ix_partidas_competicao_id_temporada",
                table: "partidas",
                columns: new[] { "competicao_id", "temporada" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consultas_diarias");

            migrationBuilder.DropTable(
                name: "escalacoes");

            migrationBuilder.DropTable(
                name: "estatisticas");

            migrationBuilder.DropTable(
                name: "partidas");

            migrationBuilder.DropTable(
                name: "respostas_provedor");

            migrationBuilder.DropTable(
                name: "atletas");

            migrationBuilder.DropTable(
                name: "clubes");

            migrationBuilder.DropTable(
                name: "competicoes");
        }
    }
}
