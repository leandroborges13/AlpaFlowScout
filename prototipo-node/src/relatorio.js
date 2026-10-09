// Gera o relatório HTML privado de uma liga/temporada já coletada.
//   node src/relatorio.js <liga> <ano>   → saida/relatorio-<liga>-<ano>.html
import fs from "node:fs";
import path from "node:path";
import { analisarTemporada, oportunidades, idadeRelativa, PERFIS, MINUTOS_MINIMOS, IDADE_MINIMA } from "./analise.js";

const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const f1 = (v) => (v === null || v === undefined ? "—" : v.toFixed(1).replace(".", ","));
const f2 = (v) => (v === null || v === undefined ? "—" : v.toFixed(2).replace(".", ","));
const pct = (v) => (v === null || v === undefined ? "—" : Math.round(v * 100) + "%");

function tabelaPosicao(posicao, atletas) {
  const ind = PERFIS[posicao];
  const linhas = atletas
    .filter((j) => j.posicao === posicao && j.nota !== null)
    .sort((a, b) => b.nota - a.nota)
    .slice(0, 15)
    .map((j, i) => `<tr>
      <td class="num">${i + 1}</td>
      <td><b>${esc(j.nome)}</b><span class="sub">${esc(j.clubes.join(" / "))}</span></td>
      <td class="num">${j.idade}</td>
      <td class="num">${j.minutos}</td>
      <td class="num">${pct(j.participacaoMinutos)}</td>
      ${ind.map((x) => `<td class="num">${x.k === "aproveitamentoDuelos" ? f1(j[x.k]) : f2(j[x.k])}</td>`).join("")}
      <td class="num nota">${f1(j.nota)}</td>
    </tr>`)
    .join("");
  return `<section>
    <h2>${posicao}s</h2>
    <div class="rolagem"><table>
      <thead><tr><th>#</th><th>Atleta</th><th>Idade*</th><th>Min</th><th>% min. do clube</th>
      ${ind.map((x) => `<th>${x.nome}</th>`).join("")}<th>Nota</th></tr></thead>
      <tbody>${linhas || `<tr><td colspan="99" class="vazio">Sem atletas elegíveis</td></tr>`}</tbody>
    </table></div>
  </section>`;
}

function gerar(liga, ano) {
  const arqDados = path.resolve("dados", `jogadores-${liga}-${ano}.json`);
  if (!fs.existsSync(arqDados)) throw new Error(`Rode antes: node --env-file=.env src/coletar.js jogadores ${liga} ${ano}`);
  const itens = JSON.parse(fs.readFileSync(arqDados, "utf8"));
  const nomeLiga = itens.flatMap((i) => i.statistics || []).find((s) => s.league?.id === Number(liga))?.league?.name || `Liga ${liga}`;

  // Idade no fim da temporada analisada: quem era menor naquele ano fica fora, mesmo que hoje já tenha 18
  const { atletas, fora, totalNaLiga } = analisarTemporada(itens, liga, { referencia: new Date(`${ano}-12-31T12:00:00`) });
  const arqCobertura = path.resolve("dados", `cobertura-${liga}-${ano}.json`);
  const cobertura = fs.existsSync(arqCobertura) ? JSON.parse(fs.readFileSync(arqCobertura, "utf8")) : [];
  const lidas = cobertura.reduce((t, c) => t + c.paginasLidas, 0);
  const existentes = cobertura.reduce((t, c) => t + c.paginasTotal, 0);
  const incompletos = cobertura.filter((c) => c.paginasLidas < c.paginasTotal);
  const avisoCobertura = incompletos.length
    ? `<p class="aviso"><b>Dados incompletos:</b> o plano gratuito da API só deixa ler 3 páginas por elenco. Foram lidas ${lidas} de ${existentes} páginas (~${Math.round((lidas / existentes) * 100)}%), e a API não ordena os atletas, então os que faltam podem ser de qualquer clube e posição. Times afetados: ${incompletos.map((c) => esc(c.time)).join(", ")}.</p>`
    : "";
  const ops = oportunidades(atletas);
  const tri = idadeRelativa(atletas);
  const maxTri = Math.max(...tri, 1);

  const html = `<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(nomeLiga)} ${ano} · AlpaFlow Scout</title>
<style>
:root { --bg:#f6f5f2; --surface:#fff; --ink:#1c1d26; --muted:#6b6862; --line:#e5e1da; --accent:#e8590c; --barra:#2f6fd6; }
@media (prefers-color-scheme: dark) { :root { --bg:#16171d; --surface:#1f2029; --ink:#efece8; --muted:#a39d96; --line:#33343e; --accent:#ff8a4c; --barra:#6ea2ff; } }
* { box-sizing:border-box } body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.45 "Segoe UI", system-ui, sans-serif; }
main { max-width:1180px; margin:0 auto; padding:28px 16px 60px; display:grid; gap:26px; }
main > * { min-width:0 }
h1 { margin:0; font-size:28px } h2 { margin:0 0 10px; font-size:19px } .lead { color:var(--muted); margin:4px 0 0 }
.aviso { background:var(--surface); border:1px solid var(--line); border-left:4px solid var(--accent); border-radius:10px; padding:10px 14px; font-size:14px }
.cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:10px }
.card { background:var(--surface); border:1px solid var(--line); border-radius:12px; padding:12px 14px } .card b { font-size:24px; display:block; font-variant-numeric:tabular-nums } .card span { color:var(--muted); font-size:13px }
.rolagem { overflow-x:auto; background:var(--surface); border:1px solid var(--line); border-radius:12px }
table { border-collapse:collapse; width:100%; font-size:13.5px } th, td { padding:7px 10px; border-bottom:1px solid var(--line); text-align:left; white-space:nowrap }
th { color:var(--muted); font-weight:600; font-size:12px } .num { text-align:right; font-variant-numeric:tabular-nums } .nota { font-weight:700; color:var(--accent) }
.sub { display:block; color:var(--muted); font-size:12px } .vazio { color:var(--muted); text-align:center }
.tri { display:grid; gap:6px } .tri div { display:grid; grid-template-columns:150px 1fr 40px; gap:10px; align-items:center; font-size:14px }
.tri i { display:block; height:12px; border-radius:0 4px 4px 0; background:var(--barra) }
</style></head><body><main>
<header>
  <h1>${esc(nomeLiga)} · ${ano}</h1>
  <p class="lead">Protótipo privado AlpaFlow Scout · dados da API-Football (uso interno, não publicar)</p>
</header>

${avisoCobertura}
<p class="aviso">Só entram atletas com ${IDADE_MINIMA} anos ou mais e pelo menos ${MINUTOS_MINIMOS} minutos. *Idade no fim da temporada. A nota é a média dos percentis do atleta nos indicadores da posição dele (100 = melhor da liga na posição).</p>

<div class="cards">
  <div class="card"><b>${totalNaLiga}</b><span>atletas na liga</span></div>
  <div class="card"><b>${atletas.length}</b><span>analisados</span></div>
  <div class="card"><b>${fora.menores}</b><span>fora por serem menores</span></div>
  <div class="card"><b>${fora.semNascimento}</b><span>fora sem data de nascimento</span></div>
  <div class="card"><b>${fora.poucosMinutos}</b><span>fora com menos de ${MINUTOS_MINIMOS} min</span></div>
</div>

<section>
  <h2>Oportunidades: até 23 anos, com 40% ou mais dos minutos do clube e nota 65 ou mais</h2>
  <div class="rolagem"><table>
    <thead><tr><th>Atleta</th><th>Posição</th><th>Idade*</th><th>Nasc.</th><th>Min</th><th>% min. do clube</th><th>Nota</th></tr></thead>
    <tbody>${ops.map((j) => `<tr><td><b>${esc(j.nome)}</b><span class="sub">${esc(j.clubes.join(" / "))}</span></td><td>${j.posicao}</td>
      <td class="num">${j.idade}</td><td class="num">${esc(j.nascimento.split("-").reverse().join("/"))}</td><td class="num">${j.minutos}</td>
      <td class="num">${pct(j.participacaoMinutos)}</td><td class="num nota">${f1(j.nota)}</td></tr>`).join("") || `<tr><td colspan="7" class="vazio">Nenhum atleta nos critérios</td></tr>`}</tbody>
  </table></div>
</section>

<section>
  <h2>Mês de nascimento dos atletas analisados</h2>
  <p class="lead">Se o 1º trimestre domina, a liga favorece quem nasceu no começo do ano: quem nasceu no fim e mesmo assim joga tende a ser subvalorizado.</p>
  <div class="tri" style="margin-top:10px">
    ${["Jan–Mar", "Abr–Jun", "Jul–Set", "Out–Dez"].map((n, i) => `<div><span>${n}</span><i style="width:${(tri[i] / maxTri) * 100}%"></i><span class="num">${tri[i]}</span></div>`).join("")}
  </div>
</section>

${Object.keys(PERFIS).map((p) => tabelaPosicao(p, atletas)).join("\n")}
</main></body></html>`;

  fs.mkdirSync(path.resolve("saida"), { recursive: true });
  const arq = path.resolve("saida", `relatorio-${liga}-${ano}.html`);
  fs.writeFileSync(arq, html);
  console.log(`${atletas.length} atletas analisados · ${ops.length} oportunidades`);
  console.log(`Relatório: ${arq}`);
}

const [liga, ano] = process.argv.slice(2);
try {
  if (!liga || !ano) throw new Error("Uso: node src/relatorio.js <liga> <ano>");
  gerar(liga, ano);
} catch (e) {
  console.error("Erro:", e.message);
  process.exit(1);
}
