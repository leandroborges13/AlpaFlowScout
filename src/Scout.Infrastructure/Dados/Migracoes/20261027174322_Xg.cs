using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Xg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "xg_lido",
                table: "partidas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "xg",
                table: "estatisticas_partida",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xg_lido",
                table: "partidas");

            migrationBuilder.DropColumn(
                name: "xg",
                table: "estatisticas_partida");
        }
    }
}
