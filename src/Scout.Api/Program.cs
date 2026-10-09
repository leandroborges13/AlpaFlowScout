using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OpenAI;
using Scout.Application;
using Scout.Application.Chat;
using Scout.Application.Consultas;
using Scout.Infrastructure;

ArquivoEnv.Carregar();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddScoutApplication();
builder.Services.AddScoutInfrastructure(builder.Configuration);

// IA: OpenAI por trás da abstração IChatClient, com chamada automática das ferramentas
var chaveOpenAi = builder.Configuration["OPENAI_API_KEY"];
var modelo = builder.Configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini";
builder.Services.AddChatClient(_ => string.IsNullOrWhiteSpace(chaveOpenAi)
        ? throw new InvalidOperationException("Defina OPENAI_API_KEY no .env para usar o chat.")
        : new OpenAIClient(chaveOpenAi).GetChatClient(modelo).AsIChatClient())
    .UseFunctionInvocation();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();

var api = app.MapGroup("/api");

api.MapGet("/competicoes", (IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>> h, CancellationToken ct) =>
    h.Handle(new ListarCompeticoes(), ct));

api.MapPost("/busca", (BuscarPorPerfil q, IQueryHandler<BuscarPorPerfil, ResultadoBusca> h, CancellationToken ct) =>
    h.Handle(q, ct));

api.MapGet("/atletas/{id:int}", async (int id, int? temporada, int? competicao, IQueryHandler<ObterAtleta, DetalheAtleta?> h, CancellationToken ct) =>
    await h.Handle(new ObterAtleta(id, temporada, competicao), ct) is { } d ? Results.Ok(d) : Results.NotFound());

api.MapGet("/atletas/{id:int}/parecidos", async (int id, int? temporada, int? idadeMax, IQueryHandler<AtletasParecidos, ResultadoParecidos?> h, CancellationToken ct) =>
    await h.Handle(new AtletasParecidos(id, temporada, idadeMax), ct) is { } r ? Results.Ok(r) : Results.NotFound());

api.MapGet("/oportunidades", (int? temporada, int? idadeMax, IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>> h, CancellationToken ct) =>
    h.Handle(new BuscarOportunidades(temporada, IdadeMax: idadeMax ?? 23), ct));

api.MapPost("/chat", async (PedidoChat pedido, AssistenteScout assistente, CancellationToken ct) =>
    pedido.Mensagens is not { Count: > 0 }
        ? Results.BadRequest("Envie ao menos uma mensagem")
        : Results.Ok(await assistente.Responder(pedido.Mensagens, ct)));

app.Run();

record PedidoChat(List<MensagemChat> Mensagens);
