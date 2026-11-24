using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scout.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ValorDeMercado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "contrato_ate",
                table: "atletas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "valor_lido_em",
                table: "atletas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_mercado",
                table: "atletas",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contrato_ate",
                table: "atletas");

            migrationBuilder.DropColumn(
                name: "valor_lido_em",
                table: "atletas");

            migrationBuilder.DropColumn(
                name: "valor_mercado",
                table: "atletas");
        }
    }
}
