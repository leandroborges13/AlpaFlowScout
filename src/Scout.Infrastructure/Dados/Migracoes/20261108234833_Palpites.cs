using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Palpites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "palpites",
                columns: table => new
                {
                    partida_id = table.Column<int>(type: "integer", nullable: false),
                    feito_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    forca_mandante = table.Column<double>(type: "double precision", nullable: false),
                    forca_visitante = table.Column<double>(type: "double precision", nullable: false),
                    chance_mandante = table.Column<double>(type: "double precision", nullable: false),
                    chance_empate = table.Column<double>(type: "double precision", nullable: false),
                    chance_visitante = table.Column<double>(type: "double precision", nullable: false),
                    placar_mandante = table.Column<int>(type: "integer", nullable: false),
                    placar_visitante = table.Column<int>(type: "integer", nullable: false),
                    chance_placar = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_palpites", x => x.partida_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "palpites");
        }
    }
}
