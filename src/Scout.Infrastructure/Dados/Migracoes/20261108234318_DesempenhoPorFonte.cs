using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class DesempenhoPorFonte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_desempenhos_avancados_partida_id_pitch_jogador_id",
                table: "desempenhos_avancados");

            migrationBuilder.RenameColumn(
                name: "pitch_jogador_id",
                table: "desempenhos_avancados",
                newName: "jogador_fonte_id");

            migrationBuilder.AddColumn<int>(
                name: "bzzoiro_id",
                table: "partidas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bzzoiro_importada",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "fonte",
                table: "desempenhos_avancados",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "PitchAPI");

            migrationBuilder.CreateIndex(
                name: "ix_desempenhos_avancados_partida_id_fonte_jogador_fonte_id",
                table: "desempenhos_avancados",
                columns: new[] { "partida_id", "fonte", "jogador_fonte_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_desempenhos_avancados_partida_id_fonte_jogador_fonte_id",
                table: "desempenhos_avancados");

            migrationBuilder.DropColumn(
                name: "bzzoiro_id",
                table: "partidas");

            migrationBuilder.DropColumn(
                name: "bzzoiro_importada",
                table: "partidas");

            migrationBuilder.DropColumn(
                name: "fonte",
                table: "desempenhos_avancados");

            migrationBuilder.RenameColumn(
                name: "jogador_fonte_id",
                table: "desempenhos_avancados",
                newName: "pitch_jogador_id");

            migrationBuilder.CreateIndex(
                name: "ix_desempenhos_avancados_partida_id_pitch_jogador_id",
                table: "desempenhos_avancados",
                columns: new[] { "partida_id", "pitch_jogador_id" },
                unique: true);
        }
    }
}
