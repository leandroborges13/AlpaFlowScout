using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.AI;
using OpenAI;
using Scout.Application;
using Scout.Application.Chat;
using Scout.Application.Consultas;
using Scout.Infrastructure;
using Scout.Infrastructure.Acesso;
using Scout.Infrastructure.Provedor;

ArquivoEnv.Carregar();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoutApplication();
builder.Services.AddScoutInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AtualizacaoDiaria>();

// Login por cookie: uso interno, só quem foi cadastrado pela linha de comando entra
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "scout";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // API não redireciona para página de login: responde 401 e a tela mostra o login
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

// Chaves do cookie em volume: sem isso, todo mundo cai do login a cada novo deploy
if (builder.Configuration["SCOUT_CHAVES_DIR"] is { Length: > 0 } pastaChaves)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(pastaChaves)).SetApplicationName("AlpaFlowScout");

// Contra tentativa e erro de senha: 5 tentativas por minuto por IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

// Atrás do Nginx Proxy Manager: confia no X-Forwarded-Proto/For para o cookie seguro e o IP real
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// IA: OpenAI por trás da abstração IChatClient, com chamada automática das ferramentas
var chaveOpenAi = builder.Configuration["OPENAI_API_KEY"];
var modelo = builder.Configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini";
builder.Services.AddChatClient(_ => string.IsNullOrWhiteSpace(chaveOpenAi)
        ? throw new InvalidOperationException("Defina OPENAI_API_KEY no .env para usar o chat.")
        : new OpenAIClient(chaveOpenAi).GetChatClient(modelo).AsIChatClient())
    .UseFunctionInvocation();

var app = builder.Build();
app.UseForwardedHeaders();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/saude", () => Results.Ok("ok"));

// ---------- Acesso ----------

app.MapPost("/api/login", async (PedidoLogin p, Usuarios usuarios, HttpContext http, CancellationToken ct) =>
{
    var u = await usuarios.Autenticar(p.Email ?? "", p.Senha ?? "", ct);
    if (u is null) return Results.Problem("E-mail ou senha incorretos.", statusCode: StatusCodes.Status401Unauthorized);
    var identidade = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, u.Id.ToString()), new(ClaimTypes.Name, u.Nome), new(ClaimTypes.Email, u.Email)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identidade), new AuthenticationProperties { IsPersistent = true });
    return Results.Ok(new { nome = u.Nome });
}).RequireRateLimiting("login");

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();
    return Results.Ok();
});

app.MapGet("/api/sessao", (ClaimsPrincipal u) => Results.Ok(new { nome = u.Identity?.Name }))
    .RequireAuthorization();

// ---------- Análises (todas exigem login) ----------

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/competicoes", (IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>> h, CancellationToken ct) =>
    h.Handle(new ListarCompeticoes(), ct));

api.MapPost("/busca", (BuscarPorPerfil q, IQueryHandler<BuscarPorPerfil, ResultadoBusca> h, CancellationToken ct) =>
    h.Handle(q, ct));

api.MapGet("/atletas/{id:int}", async (int id, int? temporada, int? competicao, IQueryHandler<ObterAtleta, DetalheAtleta?> h, CancellationToken ct) =>
    await h.Handle(new ObterAtleta(id, temporada, competicao), ct) is { } d ? Results.Ok(d) : Results.NotFound());

api.MapGet("/atletas/{id:int}/resumo-ia", async (int id, int? temporada, IQueryHandler<ObterAtleta, DetalheAtleta?> h, AssistenteScout assistente, CancellationToken ct) =>
    await h.Handle(new ObterAtleta(id, temporada), ct) is { } d
        ? Results.Ok(new { texto = await assistente.ResumirAtleta(d, ct) })
        : Results.NotFound());

api.MapGet("/atletas/{id:int}/parecidos", async (int id, int? temporada, int? idadeMax, IQueryHandler<AtletasParecidos, ResultadoParecidos?> h, CancellationToken ct) =>
    await h.Handle(new AtletasParecidos(id, temporada, idadeMax), ct) is { } r ? Results.Ok(r) : Results.NotFound());

api.MapGet("/oportunidades", (int? temporada, int? idadeMax, IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>> h, CancellationToken ct) =>
    h.Handle(new BuscarOportunidades(temporada, IdadeMax: idadeMax ?? 23), ct));

api.MapPost("/chat", async (PedidoChat pedido, AssistenteScout assistente, CancellationToken ct) =>
    pedido.Mensagens is not { Count: > 0 }
        ? Results.BadRequest("Envie ao menos uma mensagem")
        : Results.Ok(await assistente.Responder(pedido.Mensagens, ct)));

// O site (React) é servido pela própria API; qualquer rota que não seja /api cai no index.html
app.MapFallbackToFile("index.html");

app.Run();

record PedidoChat(List<MensagemChat> Mensagens);
record PedidoLogin(string? Email, string? Senha);
