using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class EstilosNoJogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estilos_no_jogo",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    clube_id = table.Column<int>(type: "integer", nullable: false),
                    fonte = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "PitchAPI"),
                    posse = table.Column<double>(type: "double precision", nullable: true),
                    field_tilt = table.Column<double>(type: "double precision", nullable: true),
                    ppda = table.Column<double>(type: "double precision", nullable: true),
                    recuperacoes_altas = table.Column<int>(type: "integer", nullable: false),
                    recuperacoes_em5s = table.Column<int>(type: "integer", nullable: false),
                    altura_defesa = table.Column<double>(type: "double precision", nullable: true),
                    ataques_diretos = table.Column<int>(type: "integer", nullable: false),
                    ataques_construidos = table.Column<int>(type: "integer", nullable: false),
                    passes_progressivos = table.Column<int>(type: "integer", nullable: false),
                    entradas_terco_final = table.Column<int>(type: "integer", nullable: false),
                    entradas_area = table.Column<int>(type: "integer", nullable: false),
                    xt = table.Column<double>(type: "double precision", nullable: true),
                    bruto = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estilos_no_jogo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_estilos_no_jogo_partida_id_clube_id_fonte",
                table: "estilos_no_jogo",
                columns: new[] { "partida_id", "clube_id", "fonte" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estilos_no_jogo");
        }
    }
}
