// Coleta de dados da API-Football para o banco do Scout e administração.
//   dotnet run --project src/Scout.Coleta -- importar <liga> <temporada> [--sem-escalacoes]
//   dotnet run --project src/Scout.Coleta -- cota
//   dotnet run --project src/Scout.Coleta -- migrar
//   dotnet run --project src/Scout.Coleta -- criar-usuario <email> <nome>   (pede a senha no terminal)
//   dotnet run --project src/Scout.Coleta -- remover-usuario <email>
//   dotnet run --project src/Scout.Coleta -- reimportar-partida <id>   (busca de novo escalação, estatísticas e lances)
//   dotnet run --project src/Scout.Coleta -- sem-nascimento   (atletas com 450+ min na liga sem data de nascimento)
//   dotnet run --project src/Scout.Coleta -- nascimento <id> <aaaa-mm-dd> <fonte>   (data de fonte pública, ex.: "site do clube")
//   dotnet run --project src/Scout.Coleta -- atleta <nome>   (o que a base tem do atleta)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scout.Infrastructure;
using Scout.Infrastructure.Dados;
using Scout.Infrastructure.Provedor;

ArquivoEnv.Carregar();
var builder = Host.CreateApplicationBuilder();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning).AddFilter("System.Net.Http", LogLevel.Warning).AddFilter("Polly", LogLevel.Warning);
builder.Services.AddScoutInfrastructure(builder.Configuration);
using var host = builder.Build();
using var escopo = host.Services.CreateScope();
var sp = escopo.ServiceProvider;
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (args.FirstOrDefault())
    {
        case "migrar":
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            Console.WriteLine("Banco atualizado.");
            break;

        case "cota":
            Console.WriteLine($"Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "importar" when args.Length >= 3 && int.TryParse(args[1], out var liga) && int.TryParse(args[2], out var temporada):
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var r = await sp.GetRequiredService<Importador>().Importar(liga, temporada, !args.Contains("--sem-escalacoes"), cts.Token);
            Console.WriteLine($"{r.Competicao} {r.Temporada}: {r.Clubes} clubes, {r.Atletas} atletas, {r.Partidas} partidas ({r.PartidasComEscalacao} com escalação)");
            Console.WriteLine($"Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "atleta" when args.Length >= 2:
            await Diagnostico.Atleta(sp.GetRequiredService<ScoutDbContext>(), string.Join(' ', args[1..]), cts.Token);
            break;

        case "nascimento" when args.Length >= 4 && int.TryParse(args[1], out var idAtleta) && DateOnly.TryParseExact(args[2], "yyyy-MM-dd", out var data):
            // Data de uma fonte pública (site do clube, BID da CBF) para atleta que o provedor não tem
            var dbN = sp.GetRequiredService<ScoutDbContext>();
            var atletaN = await dbN.Atletas.FindAsync([idAtleta], cts.Token);
            if (atletaN is null) { Console.Error.WriteLine("Atleta não encontrado."); return 1; }
            atletaN.Nascimento = data;
            atletaN.FonteNascimento = string.Join(' ', args[3..]);
            await dbN.SaveChangesAsync(cts.Token);
            Console.WriteLine($"{atletaN.Nome}: nascimento {data:dd/MM/yyyy} (fonte: {atletaN.FonteNascimento})");
            break;

        case "sem-nascimento":
            await Diagnostico.SemNascimento(sp.GetRequiredService<ScoutDbContext>(), cts.Token);
            break;

        case "diagnostico" when args.Length >= 3 && int.TryParse(args[1], out var ligaD) && int.TryParse(args[2], out var temporadaD):
            var dbD = sp.GetRequiredService<ScoutDbContext>();
            await Diagnostico.CompeticoesDosElencos(dbD, ligaD, temporadaD, cts.Token);
            await Diagnostico.ConsistenciaDosLados(dbD, ligaD, temporadaD, cts.Token);
            break;

        case "criar-usuario" when args.Length >= 3:
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            // A senha é digitada aqui, sem aparecer na tela e sem passar por argumento (não fica no histórico)
            Console.Write("Senha: ");
            var senha = LerSenha();
            Console.Write("Repita a senha: ");
            if (LerSenha() != senha) { Console.Error.WriteLine("As senhas não conferem."); return 1; }
            var u = await sp.GetRequiredService<Scout.Infrastructure.Acesso.Usuarios>().Criar(args[1], string.Join(' ', args[2..]), senha, cts.Token);
            Console.WriteLine($"Usuário pronto: {u.Email}");
            break;

        case "reimportar-partida" when args.Length >= 2 && int.TryParse(args[1], out var partidaId):
            await sp.GetRequiredService<Importador>().ReimportarPartida(partidaId, cts.Token);
            Console.WriteLine($"Partida {partidaId} reimportada. Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "remover-usuario" when args.Length >= 2:
            var removido = await sp.GetRequiredService<Scout.Infrastructure.Acesso.Usuarios>().Remover(args[1], cts.Token);
            Console.WriteLine(removido ? $"Usuário removido: {args[1]}" : "Usuário não encontrado.");
            break;

        default:
            Console.WriteLine("Comandos: migrar | cota | importar <liga> <temporada> [--sem-escalacoes] | criar-usuario <email> <nome> | remover-usuario <email>");
            Console.WriteLine("Ligas: 71 Série A · 72 Série B · 75 Série C · 76 Série D");
            return 1;
    }
    return 0;
}
catch (CotaEsgotadaException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static string LerSenha()
{
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var senha = new System.Text.StringBuilder();
    while (true)
    {
        var tecla = Console.ReadKey(intercept: true);
        if (tecla.Key == ConsoleKey.Enter) break;
        if (tecla.Key == ConsoleKey.Backspace) { if (senha.Length > 0) senha.Length--; continue; }
        if (!char.IsControl(tecla.KeyChar)) senha.Append(tecla.KeyChar);
    }
    Console.WriteLine();
    return senha.ToString();
}
