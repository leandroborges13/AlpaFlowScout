// Coleta de dados da API-Football para o banco do Scout e administração.
//   dotnet run --project src/Scout.Coleta -- importar <liga> <temporada> [--sem-escalacoes]
//   dotnet run --project src/Scout.Coleta -- cota
//   dotnet run --project src/Scout.Coleta -- placares <liga> <temporada>   (só os placares; ex.: 13 2025 e 11 2025 para a força das ligas)
//   dotnet run --project src/Scout.Coleta -- migrar
//   dotnet run --project src/Scout.Coleta -- criar-usuario <email> <nome>   (pede a senha no terminal)
//   dotnet run --project src/Scout.Coleta -- remover-usuario <email>
//   dotnet run --project src/Scout.Coleta -- reimportar-partida <id>   (busca de novo escalação, estatísticas e lances)
//   dotnet run --project src/Scout.Coleta -- sem-nascimento   (atletas com 450+ min na liga sem data de nascimento)
//   dotnet run --project src/Scout.Coleta -- nascimento <id> <aaaa-mm-dd> <fonte>   (data de fonte pública, ex.: "site do clube")
//   dotnet run --project src/Scout.Coleta -- atleta <nome>   (o que a base tem do atleta)
//   dotnet run --project src/Scout.Coleta -- pitch <liga> <temporada> [max]   (desempenho avançado jogo a jogo pela PitchAPI)
//   dotnet run --project src/Scout.Coleta -- pitch-estilos <liga> <temporada> [max]   (métricas de time jogo a jogo: pressão, recuperações altas)
//   dotnet run --project src/Scout.Coleta -- estilo <clube>   (estilo do time na liga: pressão, recuperações altas, com o ranking)
//   dotnet run --project src/Scout.Coleta -- comentar <partida> [time]   (os pontos do "Comentar o jogo", com a frase pronta)
//   dotnet run --project src/Scout.Coleta -- nascimentos-bzzoiro <temporada>   (data de nascimento de quem está sem, pela Bzzoiro)
//   dotnet run --project src/Scout.Coleta -- copas-bzzoiro <temporada> [max]   (desempenho dos atletas nos jogos de copa, pela Bzzoiro)
//   dotnet run --project src/Scout.Coleta -- valores-bzzoiro [max-clubes]   (valor de mercado e fim de contrato, pela Bzzoiro)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scout.Application;
using Scout.Infrastructure;
using Scout.Infrastructure.Dados;
using Scout.Infrastructure.Provedor;

ArquivoEnv.Carregar();
var builder = Host.CreateApplicationBuilder();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning).AddFilter("System.Net.Http", LogLevel.Warning).AddFilter("Polly", LogLevel.Warning);
builder.Services.AddScoutApplication(); // consultas usadas pelos comandos (ex.: comentar); o chat não é resolvido aqui
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

        case "placares" when args.Length >= 3 && int.TryParse(args[1], out var ligaPl) && int.TryParse(args[2], out var temporadaPl):
            // só os placares (ex.: Libertadores 13 e Sul-Americana 11 de 2025, para a força das ligas)
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            Console.WriteLine($"{await sp.GetRequiredService<Importador>().ImportarPlacares(ligaPl, temporadaPl, cts.Token)} jogos. Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "pitch" when args.Length >= 3 && int.TryParse(args[1], out var ligaP) && int.TryParse(args[2], out var temporadaP):
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var maxP = args.Length >= 4 && int.TryParse(args[3], out var m) ? m : 400;
            var rp = await sp.GetRequiredService<ColetaPitch>().Coletar(ligaP, temporadaP, maxP, cts.Token);
            Console.WriteLine($"PitchAPI: {rp.Jogos} jogos, {rp.Ligados}/{rp.Atletas} atletas ligados ao cadastro, {rp.SemPar} jogos sem par na base");
            break;

        case "pitch-estilos" when args.Length >= 3 && int.TryParse(args[1], out var ligaE) && int.TryParse(args[2], out var temporadaE):
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var maxE = args.Length >= 4 && int.TryParse(args[3], out var me) ? me : 400;
            Console.WriteLine($"PitchAPI estilos: {await sp.GetRequiredService<ColetaPitch>().ColetarEstilos(ligaE, temporadaE, maxE, cts.Token)} jogos");
            break;

        case "nascimentos-bzzoiro" when args.Length >= 2 && int.TryParse(args[1], out var temporadaB):
            var rb = await sp.GetRequiredService<ColetaBzzoiro>().PreencherNascimentos(temporadaB, cts.Token);
            Console.WriteLine($"Bzzoiro: {rb.Preenchidos} de {rb.SemData} atletas sem data preenchidos; {rb.ClubesLigados}/{rb.Clubes} clubes ligados; {rb.Duvidosos} com datas conflitantes");
            break;

        case "valores-bzzoiro":
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var rv = await sp.GetRequiredService<ColetaBzzoiro>().PreencherValores(args.Length >= 2 && int.TryParse(args[1], out var maxV) ? maxV : 1000, cts.Token);
            Console.WriteLine($"Bzzoiro: {rv.ComValor} de {rv.Atletas} atletas com valor de mercado; {rv.ClubesLigados}/{rv.Clubes} clubes ligados");
            break;

        case "copas-bzzoiro" when args.Length >= 2 && int.TryParse(args[1], out var temporadaC):
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var maxC = args.Length >= 3 && int.TryParse(args[2], out var mc) ? mc : 200;
            var rc = await sp.GetRequiredService<ColetaBzzoiro>().ColetarCopas(temporadaC, maxC, cts.Token);
            Console.WriteLine($"Bzzoiro copas: {rc.Jogos} jogos, {rc.Ligados}/{rc.Atletas} atletas ligados ao cadastro, {rc.SemPar} jogos sem par na base");
            break;

        case "comentar" when args.Length >= 2 && int.TryParse(args[1], out var partidaCom):
            // os pontos do "Comentar o jogo" (o mesmo do botão do painel), sem login: para o pós-jogo pelo terminal
            var com = await sp.GetRequiredService<Scout.Application.Chat.PontosParaComentar>()
                .Gerar(partidaCom, args.Length >= 3 && int.TryParse(args[2], out var tc) ? tc : null, cts.Token);
            if (com is null) { Console.WriteLine("Jogo sem placar ou não encontrado."); break; }
            Console.WriteLine($"{com.Partida.Mandante} x {com.Partida.Visitante} · do ponto de vista do {com.Time}");
            foreach (var pt in com.Pontos) Console.WriteLine($"- [{pt.Tema}] {pt.Frase}");
            break;

        case "estilo" when args.Length >= 2 && int.TryParse(args[1], out var clubeEst):
            // o estilo do time na liga, como a tela mostra (métricas de time da PitchAPI ranqueadas)
            var est = await new Scout.Application.Consultas.ObterEstiloNaLigaHandler(sp.GetRequiredService<ScoutDbContext>())
                .Handle(new Scout.Application.Consultas.ObterEstiloNaLiga(clubeEst), cts.Token);
            if (est is null) { Console.WriteLine("Sem métricas de time para esse clube."); break; }
            Console.WriteLine($"{est.Time} · {est.Competicao} {est.Temporada} · {est.Jogos} jogos");
            foreach (var mt in est.Metricas) Console.WriteLine($"  {mt.Posicao,2}º de {mt.Total} | {mt.Nome}: {mt.Valor} (média {mt.MediaDaLiga}) | {mt.Frase}");
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
            Console.WriteLine("Ligas: 71 Série A · 72 Série B · 75 Série C · 76 Série D · 128 Argentina · 239 Colômbia · 242 Equador · 94 Portugal · 262 México · 253 MLS");
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
