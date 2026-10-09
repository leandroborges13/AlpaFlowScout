// Métricas e perfis a partir das estatísticas de temporada da API-Football.
// Regra de proteção: atleta menor de 18 anos (ou sem data de nascimento) não entra em nenhuma análise individual.

export const MINUTOS_MINIMOS = 450; // ~5 jogos completos para a amostra valer
export const IDADE_MINIMA = 18;

const POSICOES = { Goalkeeper: "Goleiro", Defender: "Defensor", Midfielder: "Meio-campista", Attacker: "Atacante" };

// Indicadores usados no perfil de cada posição. `inverso` = quanto menor, melhor.
export const PERFIS = {
  Goleiro: [
    { k: "defesas90", nome: "Defesas/90" },
    { k: "golsSofridos90", nome: "Gols sofridos/90", inverso: true },
  ],
  Defensor: [
    { k: "desarmes90", nome: "Desarmes/90" },
    { k: "interceptacoes90", nome: "Interceptações/90" },
    { k: "duelosGanhos90", nome: "Duelos ganhos/90" },
    { k: "aproveitamentoDuelos", nome: "% duelos ganhos" },
    { k: "faltasCometidas90", nome: "Faltas cometidas/90", inverso: true },
  ],
  "Meio-campista": [
    { k: "passesDecisivos90", nome: "Passes decisivos/90" },
    { k: "desarmes90", nome: "Desarmes/90" },
    { k: "interceptacoes90", nome: "Interceptações/90" },
    { k: "dribles90", nome: "Dribles certos/90" },
    { k: "duelosGanhos90", nome: "Duelos ganhos/90" },
  ],
  Atacante: [
    { k: "gols90", nome: "Gols/90" },
    { k: "assistencias90", nome: "Assistências/90" },
    { k: "finalizacoesNoAlvo90", nome: "Finalizações no alvo/90" },
    { k: "passesDecisivos90", nome: "Passes decisivos/90" },
    { k: "dribles90", nome: "Dribles certos/90" },
    { k: "faltasSofridas90", nome: "Faltas sofridas/90" },
  ],
};

const num = (v) => Number(v) || 0;

export function idadeEm(nascimento, referencia = new Date()) {
  if (!nascimento) return null;
  const n = new Date(nascimento + "T12:00:00");
  if (Number.isNaN(n.getTime())) return null;
  let idade = referencia.getFullYear() - n.getFullYear();
  const aindaNaoFez = referencia.getMonth() < n.getMonth() || (referencia.getMonth() === n.getMonth() && referencia.getDate() < n.getDate());
  return aindaNaoFez ? idade - 1 : idade;
}

/**
 * Junta as linhas de estatística do atleta na liga (ele pode ter jogado por dois clubes na mesma temporada)
 * e devolve um registro plano. Retorna null se não houver estatística nessa liga.
 */
export function normalizar(item, ligaId) {
  const linhas = (item.statistics || []).filter((s) => !ligaId || s.league?.id === Number(ligaId));
  if (!linhas.length) return null;
  const soma = (f) => linhas.reduce((t, s) => t + num(f(s)), 0);
  const principal = [...linhas].sort((a, b) => num(b.games?.minutes) - num(a.games?.minutes))[0];
  const p = item.player || {};
  return {
    id: p.id,
    nome: p.name,
    nomeCompleto: [p.firstname, p.lastname].filter(Boolean).join(" ") || p.name,
    nascimento: p.birth?.date || null,
    nacionalidade: p.nationality || null,
    altura: p.height || null,
    clube: principal.team?.name,
    clubes: [...new Set(linhas.map((s) => s.team?.name).filter(Boolean))],
    posicao: POSICOES[principal.games?.position] || null,
    jogos: soma((s) => s.games?.appearences),
    titular: soma((s) => s.games?.lineups),
    minutos: soma((s) => s.games?.minutes),
    gols: soma((s) => s.goals?.total),
    assistencias: soma((s) => s.goals?.assists),
    golsSofridos: soma((s) => s.goals?.conceded),
    defesas: soma((s) => s.goals?.saves),
    finalizacoes: soma((s) => s.shots?.total),
    finalizacoesNoAlvo: soma((s) => s.shots?.on),
    passesDecisivos: soma((s) => s.passes?.key),
    desarmes: soma((s) => s.tackles?.total),
    interceptacoes: soma((s) => s.tackles?.interceptions),
    duelos: soma((s) => s.duels?.total),
    duelosGanhos: soma((s) => s.duels?.won),
    dribles: soma((s) => s.dribbles?.success),
    faltasCometidas: soma((s) => s.fouls?.committed),
    faltasSofridas: soma((s) => s.fouls?.drawn),
    amarelos: soma((s) => s.cards?.yellow),
    vermelhos: soma((s) => s.cards?.red) + soma((s) => s.cards?.yellowred),
  };
}

function por90(j) {
  const f = j.minutos > 0 ? 90 / j.minutos : 0;
  return {
    gols90: j.gols * f,
    assistencias90: j.assistencias * f,
    golsSofridos90: j.golsSofridos * f,
    defesas90: j.defesas * f,
    finalizacoesNoAlvo90: j.finalizacoesNoAlvo * f,
    passesDecisivos90: j.passesDecisivos * f,
    desarmes90: j.desarmes * f,
    interceptacoes90: j.interceptacoes * f,
    duelosGanhos90: j.duelosGanhos * f,
    aproveitamentoDuelos: j.duelos >= 20 ? (j.duelosGanhos / j.duelos) * 100 : null,
    dribles90: j.dribles * f,
    faltasCometidas90: j.faltasCometidas * f,
    faltasSofridas90: j.faltasSofridas * f,
  };
}

/** Percentil (0–100) de `valor` dentro de `valores`; empates contam pela metade. */
function percentil(valores, valor) {
  if (valor === null || valor === undefined) return null;
  const validos = valores.filter((v) => v !== null && v !== undefined);
  if (validos.length < 2) return null;
  const abaixo = validos.filter((v) => v < valor).length;
  const iguais = validos.filter((v) => v === valor).length;
  return ((abaixo + (iguais - 1) / 2) / (validos.length - 1)) * 100;
}

/**
 * Analisa uma liga/temporada. Devolve os atletas elegíveis com métricas por 90, percentis por posição,
 * nota do perfil e participação nos minutos do clube, além de quantos ficaram de fora e por quê.
 */
export function analisarTemporada(itens, ligaId, { referencia = new Date() } = {}) {
  const todos = itens.map((i) => normalizar(i, ligaId)).filter(Boolean);
  const fora = { menores: 0, semNascimento: 0, poucosMinutos: 0, semPosicao: 0 };

  // Minutos disponíveis por clube ≈ maior número de jogos de um atleta do clube × 90
  const jogosDoClube = {};
  for (const j of todos) jogosDoClube[j.clube] = Math.max(jogosDoClube[j.clube] || 0, j.jogos);

  const elegiveis = [];
  for (const j of todos) {
    const idade = idadeEm(j.nascimento, referencia);
    if (idade === null) { fora.semNascimento++; continue; }
    if (idade < IDADE_MINIMA) { fora.menores++; continue; }
    if (!j.posicao) { fora.semPosicao++; continue; }
    if (j.minutos < MINUTOS_MINIMOS) { fora.poucosMinutos++; continue; }
    const mesNasc = Number(j.nascimento.slice(5, 7));
    elegiveis.push({
      ...j,
      idade,
      mesNascimento: mesNasc,
      semestreNascimento: mesNasc <= 6 ? 1 : 2,
      participacaoMinutos: jogosDoClube[j.clube] ? j.minutos / (jogosDoClube[j.clube] * 90) : null,
      ...por90(j),
    });
  }

  // Percentis e nota dentro de cada posição (só se compara zagueiro com zagueiro)
  for (const [posicao, indicadores] of Object.entries(PERFIS)) {
    const grupo = elegiveis.filter((j) => j.posicao === posicao);
    for (const j of grupo) {
      j.percentis = {};
      const ps = [];
      for (const ind of indicadores) {
        let p = percentil(grupo.map((g) => g[ind.k]), j[ind.k]);
        if (p !== null && ind.inverso) p = 100 - p;
        j.percentis[ind.k] = p;
        if (p !== null) ps.push(p);
      }
      j.nota = ps.length ? ps.reduce((a, b) => a + b, 0) / ps.length : null;
    }
  }

  return { atletas: elegiveis, fora, totalNaLiga: todos.length };
}

/**
 * Oportunidades: jovens (até 23 anos) que já são peça importante no clube
 * (≥ 40% dos minutos) e estão entre os melhores perfis da posição (nota ≥ 65).
 */
export function oportunidades(atletas, { idadeMax = 23, participacaoMin = 0.4, notaMin = 65 } = {}) {
  return atletas
    .filter((j) => j.idade <= idadeMax && (j.participacaoMinutos ?? 0) >= participacaoMin && (j.nota ?? 0) >= notaMin)
    .sort((a, b) => b.nota - a.nota);
}

/** Distribuição do mês de nascimento por trimestre: mostra se a liga favorece quem nasceu no início do ano. */
export function idadeRelativa(atletas) {
  const t = [0, 0, 0, 0];
  for (const j of atletas) t[Math.floor((j.mesNascimento - 1) / 3)]++;
  return t;
}
