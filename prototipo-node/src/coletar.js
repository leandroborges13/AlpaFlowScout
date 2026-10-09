// Coleta da API-Football para o protótipo privado.
//   node --env-file=.env src/coletar.js status                 → plano e consultas usadas hoje (não gasta cota)
//   node --env-file=.env src/coletar.js ligas                  → competições do Brasil e o que cada temporada cobre
//   node --env-file=.env src/coletar.js jogadores <liga> <ano> → estatísticas de temporada de todos os jogadores
import fs from "node:fs";
import path from "node:path";
import { consultar, consultarTodasPaginas, cotaDoDia } from "./api.js";

const PASTA_DADOS = path.resolve("dados");
const salvar = (nome, dados) => {
  fs.mkdirSync(PASTA_DADOS, { recursive: true });
  const arq = path.join(PASTA_DADOS, nome);
  fs.writeFileSync(arq, JSON.stringify(dados, null, 2));
  return arq;
};

async function status() {
  const { response: r } = await consultar("/status", {}, { validadeHoras: 0 });
  console.log(`Plano: ${r.subscription?.plan} · ativo: ${r.subscription?.active} · fim: ${r.subscription?.end}`);
  console.log(`Consultas hoje (API): ${r.requests?.current}/${r.requests?.limit_day}`);
  const c = cotaDoDia();
  console.log(`Consultas hoje (contador local): ${c.usadas}/${c.limite}`);
}

async function ligas() {
  const { response } = await consultar("/leagues", { country: "Brazil" }, { validadeHoras: 24 * 7 });
  const lista = response.map(({ league, seasons }) => ({
    id: league.id,
    nome: league.name,
    tipo: league.type,
    temporadas: (seasons || []).map((s) => ({
      ano: s.year,
      jogadores: !!s.coverage?.players,
      estatisticasJogadorPorJogo: !!s.coverage?.fixtures?.statistics_players,
      escalacoes: !!s.coverage?.fixtures?.lineups,
    })),
  }));
  const arq = salvar("ligas-brasil.json", lista);
  for (const l of lista.sort((a, b) => a.id - b.id)) {
    const comJogadores = l.temporadas.filter((t) => t.jogadores).map((t) => t.ano);
    console.log(`${String(l.id).padStart(5)}  ${l.nome.padEnd(40)} ${l.tipo.padEnd(7)} jogadores: ${comJogadores.join(", ") || "—"}`);
  }
  console.log(`\n${lista.length} competições salvas em ${arq}`);
}

async function jogadores(liga, ano) {
  if (!liga || !ano) throw new Error("Uso: coletar.js jogadores <idDaLiga> <ano>");
  const anoAtual = new Date().getFullYear();
  const opcoes = { validadeHoras: Number(ano) >= anoAtual ? 24 : null }; // temporada em andamento: atualiza 1x por dia
  // Time a time: o plano gratuito só deixa ler 3 páginas por consulta, e um elenco cabe nisso
  const { response: times } = await consultar("/teams", { league: liga, season: ano }, opcoes);
  console.log(`Coletando jogadores da liga ${liga}, temporada ${ano}: ${times.length} times`);
  const porId = new Map();
  const cobertura = [];
  for (const { team } of times) {
    const r = await consultarTodasPaginas("/players", { team: team.id, season: ano }, opcoes);
    for (const item of r.itens) porId.set(item.player.id, item); // quem trocou de time na liga aparece nos dois elencos
    cobertura.push({ time: team.name, paginasLidas: r.paginasLidas, paginasTotal: r.paginasTotal });
    console.log(`- ${team.name}: ${r.paginasLidas}/${r.paginasTotal} páginas`);
  }
  const itens = [...porId.values()];
  salvar(`cobertura-${liga}-${ano}.json`, cobertura);
  const arq = salvar(`jogadores-${liga}-${ano}.json`, itens);
  console.log(`${itens.length} jogadores salvos em ${arq}`);
  console.log(`Cota: ${cotaDoDia().usadas}/${cotaDoDia().limite} hoje`);
}

const [comando, ...args] = process.argv.slice(2);
const comandos = { status, ligas, jogadores };
if (!comandos[comando]) {
  console.log("Comandos: status | ligas | jogadores <liga> <ano>");
  process.exit(1);
}
comandos[comando](...args).catch((e) => {
  console.error("Erro:", e.message);
  process.exitCode = 1;
});
