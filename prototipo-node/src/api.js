// Cliente da API-Football (v3) com cache em disco e controle da cota diária.
// O plano gratuito permite ~100 consultas/dia e 10/minuto: toda resposta fica salva em cache/api,
// então a mesma consulta nunca é paga duas vezes.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const BASE = "https://v3.football.api-sports.io";
const PASTA_CACHE = path.resolve("cache/api");
const ARQ_COTA = path.resolve("cache/cota.json");
const LIMITE_DIA = Number(process.env.APIFOOTBALL_LIMITE_DIA || 95); // folga sobre os 100 do plano gratuito
const INTERVALO_MS = 6500; // 10 por minuto no plano gratuito

const hoje = () => new Date().toLocaleDateString("sv-SE", { timeZone: "America/Sao_Paulo" });
let ultimaChamada = 0;

function lerCota() {
  try {
    const c = JSON.parse(fs.readFileSync(ARQ_COTA, "utf8"));
    return c.dia === hoje() ? c : { dia: hoje(), usadas: 0 };
  } catch {
    return { dia: hoje(), usadas: 0 };
  }
}

function salvarCota(c) {
  fs.mkdirSync(path.dirname(ARQ_COTA), { recursive: true });
  fs.writeFileSync(ARQ_COTA, JSON.stringify(c, null, 2));
}

function arquivoCache(endpoint, params) {
  const chave = endpoint + "?" + new URLSearchParams(Object.entries(params).sort()).toString();
  const nome = endpoint.replace(/\W+/g, "_") + "-" + crypto.createHash("sha1").update(chave).digest("hex").slice(0, 12) + ".json";
  return path.join(PASTA_CACHE, nome);
}

/**
 * GET na API com cache. `validadeHoras` = null mantém o cache para sempre
 * (temporadas encerradas não mudam); use um número para temporadas em andamento.
 */
export async function consultar(endpoint, params = {}, { validadeHoras = null } = {}) {
  const arq = arquivoCache(endpoint, params);
  if (fs.existsSync(arq)) {
    const idadeH = (Date.now() - fs.statSync(arq).mtimeMs) / 3600000;
    if (validadeHoras === null || idadeH < validadeHoras) return JSON.parse(fs.readFileSync(arq, "utf8"));
  }

  const chave = process.env.APIFOOTBALL_KEY;
  if (!chave) throw new Error("Defina APIFOOTBALL_KEY no arquivo .env (veja .env.example)");
  const cota = lerCota();
  if (endpoint !== "/status" && cota.usadas >= LIMITE_DIA) {
    throw new Error(`Cota do dia atingida (${cota.usadas}/${LIMITE_DIA}). Rode de novo amanhã: o que já veio está em cache.`);
  }

  const espera = ultimaChamada + INTERVALO_MS - Date.now();
  if (espera > 0) await new Promise((r) => setTimeout(r, espera));
  ultimaChamada = Date.now();

  const url = `${BASE}${endpoint}?${new URLSearchParams(params)}`;
  const resp = await fetch(url, { headers: { "x-apisports-key": chave } });
  if (!resp.ok) throw new Error(`API respondeu ${resp.status} em ${endpoint}`);
  const json = await resp.json();
  if (endpoint !== "/status") salvarCota({ dia: cota.dia, usadas: cota.usadas + 1 });

  // A API devolve 200 com o problema em `errors` (chave inválida, temporada fora do plano...)
  const erros = Array.isArray(json.errors) ? json.errors : Object.values(json.errors || {});
  if (erros.length) throw new Error(`API-Football: ${erros.join(" | ")}`);

  fs.mkdirSync(PASTA_CACHE, { recursive: true });
  fs.writeFileSync(arq, JSON.stringify(json));
  return json;
}

// O plano gratuito só deixa ler as 3 primeiras páginas de cada consulta
const MAX_PAGINAS = Number(process.env.APIFOOTBALL_MAX_PAGINAS || 3);

/**
 * Percorre as páginas de um endpoint paginado. Devolve os itens e quantas páginas existiam,
 * para quem chamou saber se a lista ficou incompleta por causa do limite do plano.
 */
export async function consultarTodasPaginas(endpoint, params, opcoes) {
  const itens = [];
  let total = 1;
  for (let pagina = 1; pagina <= Math.min(total, MAX_PAGINAS); pagina++) {
    const json = await consultar(endpoint, { ...params, page: pagina }, opcoes);
    itens.push(...(json.response || []));
    total = json.paging?.total ?? 1;
  }
  return { itens, paginasLidas: Math.min(total, MAX_PAGINAS), paginasTotal: total };
}

export const cotaDoDia = () => ({ ...lerCota(), limite: LIMITE_DIA });
