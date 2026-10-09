// Testes da análise com atletas fictícios no formato da resposta /players da API-Football.
import { test } from "node:test";
import assert from "node:assert/strict";
import { analisarTemporada, oportunidades, idadeEm, normalizar } from "../src/analise.js";

const REF = new Date("2026-10-08T12:00:00");
const LIGA = 72;

function atleta(id, { nascimento, posicao = "Defender", minutos = 900, jogos = 10, clube = "Clube Fictício A", desarmes = 10, interceptacoes = 5, duelos = 60, ganhos = 30, faltas = 8, extra = [] }) {
  return {
    player: { id, name: `Atleta Teste ${id}`, birth: { date: nascimento } },
    statistics: [
      {
        team: { id: 1, name: clube },
        league: { id: LIGA, name: "Liga Fictícia" },
        games: { appearences: jogos, lineups: jogos, minutes: minutos, position: posicao },
        goals: { total: 0, assists: 0, conceded: 0, saves: 0 },
        shots: { total: 0, on: 0 },
        passes: { key: 0 },
        tackles: { total: desarmes, interceptions: interceptacoes },
        duels: { total: duelos, won: ganhos },
        dribbles: { success: 0 },
        fouls: { committed: faltas, drawn: 0 },
        cards: { yellow: 0, red: 0, yellowred: 0 },
      },
      ...extra,
    ],
  };
}

test("idade considera se o aniversário já passou no ano", () => {
  assert.equal(idadeEm("2008-10-08", REF), 18);
  assert.equal(idadeEm("2008-10-09", REF), 17);
  assert.equal(idadeEm(null, REF), null);
});

test("menores de 18 e atletas sem data de nascimento ficam fora de qualquer análise", () => {
  const itens = [
    atleta(1, { nascimento: "2000-03-01" }),
    atleta(2, { nascimento: "2009-01-01" }), // 17 anos
    atleta(3, { nascimento: null }),
    atleta(4, { nascimento: "2001-05-01", minutos: 200 }),
  ];
  const r = analisarTemporada(itens, LIGA, { referencia: REF });
  assert.deepEqual(r.atletas.map((a) => a.id), [1]);
  assert.deepEqual(r.fora, { menores: 1, semNascimento: 1, poucosMinutos: 1, semPosicao: 0 });
});

test("soma as estatísticas do atleta que passou por dois clubes na mesma liga", () => {
  const segundoClube = { ...atleta(9, { nascimento: "2000-01-01" }).statistics[0], team: { id: 2, name: "Clube Fictício B" } };
  const outraLiga = { ...segundoClube, league: { id: 999 } };
  const j = normalizar(atleta(9, { nascimento: "2000-01-01", extra: [segundoClube, outraLiga] }), LIGA);
  assert.equal(j.minutos, 1800);
  assert.equal(j.desarmes, 20);
  assert.deepEqual(j.clubes, ["Clube Fictício A", "Clube Fictício B"]);
});

test("nota por posição: melhor defensor fica com nota maior; faltas contam ao contrário", () => {
  const itens = [
    atleta(1, { nascimento: "2000-01-01", desarmes: 30, interceptacoes: 15, ganhos: 45, faltas: 2 }),
    atleta(2, { nascimento: "2000-01-01", desarmes: 10, interceptacoes: 5, ganhos: 30, faltas: 8 }),
    atleta(3, { nascimento: "2000-01-01", desarmes: 2, interceptacoes: 1, ganhos: 15, faltas: 20 }),
  ];
  const { atletas } = analisarTemporada(itens, LIGA, { referencia: REF });
  const nota = Object.fromEntries(atletas.map((a) => [a.id, a.nota]));
  assert.equal(nota[1], 100);
  assert.equal(nota[3], 0);
  assert.ok(nota[2] > 0 && nota[2] < 100);
});

test("oportunidades: jovem, titular no clube e bem avaliado", () => {
  const itens = [
    atleta(1, { nascimento: "2004-08-01", desarmes: 30, interceptacoes: 15, ganhos: 45, faltas: 2 }), // 22 anos, melhor
    atleta(2, { nascimento: "1995-01-01", desarmes: 29, interceptacoes: 14, ganhos: 44, faltas: 3 }), // bom, mas 31 anos
    atleta(3, { nascimento: "2005-01-01", desarmes: 2, interceptacoes: 1, ganhos: 15, faltas: 20 }), // jovem, nota baixa
  ];
  const { atletas } = analisarTemporada(itens, LIGA, { referencia: REF });
  assert.deepEqual(oportunidades(atletas).map((a) => a.id), [1]);
});
