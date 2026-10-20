import { useState } from 'react'
import type { AnalisePartida, DesempenhoNoJogo, EventoDaPartida, IndicadorNoJogo, LadoDaPartida } from './api'
import { Medidor } from './Componentes'

// Campo deitado (105 x 68 m): o mandante ataca para a direita, o visitante para a esquerda.
const C = 105
const L = 68

const ICONE: Record<string, string> = {
  'Gol': '⚽', 'Gol de pênalti': '⚽', 'Gol contra': '⚽', 'Pênalti perdido': '✕',
  'Cartão amarelo': '🟨', 'Cartão vermelho': '🟥', 'Substituição': '⇄', 'VAR': 'VAR',
}

/** Nome que cabe no campinho: "K. Pascini" → "Pascini"; nome longo fica só com o último nome. */
const curto = (nome: string) => {
  const semInicial = nome.includes('. ') ? nome.slice(nome.lastIndexOf('. ') + 2) : nome
  const n = semInicial.length > 11 ? semInicial.split(/\s+/).pop() ?? semInicial : semInicial
  return n.length > 12 ? `${n.slice(0, 11)}…` : n
}

type Selecao = { selecionado: number | null; onSelecionar: (id: number) => void }

/** Os 22 titulares no desenho tático de cada time, com a nota do jogo. Toque num atleta para ver o desempenho. */
export function CampoDaPartida({ a, notas, selecionado, onSelecionar }: { a: AnalisePartida; notas: Map<number, DesempenhoNoJogo> } & Selecao) {
  const lados: [LadoDaPartida, 'casa' | 'fora'][] = [[a.mandante, 'casa'], [a.visitante, 'fora']]
  const temPosicao = lados.some(([l]) => l.titulares.some((j) => j.x != null))
  if (!temPosicao) return null

  return (
    <div className="campo-partida">
      <div className="campo-partida-times">
        <span className="time-casa">{a.mandante.time}{a.mandante.esquema ? ` · ${a.mandante.esquema}` : ''} →</span>
        <span className="time-fora">← {a.visitante.time}{a.visitante.esquema ? ` · ${a.visitante.esquema}` : ''}</span>
      </div>
      <div className="campo-rolagem">
      <svg viewBox={`-1 -1 ${C + 2} ${L + 2}`} className="campo-deitado" role="img"
        aria-label={`Escalações em campo: ${a.mandante.time} à esquerda atacando para a direita, ${a.visitante.time} à direita`}>
        <rect x={0} y={0} width={C} height={L} rx={1.2} className="gramado" />
        <g className="linhas">
          <rect x={0} y={0} width={C} height={L} rx={1.2} />
          <line x1={C / 2} y1={0} x2={C / 2} y2={L} />
          <circle cx={C / 2} cy={L / 2} r={9.15} />
          <rect x={0} y={L / 2 - 20.16} width={16.5} height={40.32} />
          <rect x={0} y={L / 2 - 9.16} width={5.5} height={18.32} />
          <rect x={C - 16.5} y={L / 2 - 20.16} width={16.5} height={40.32} />
          <rect x={C - 5.5} y={L / 2 - 9.16} width={5.5} height={18.32} />
        </g>
        {lados.map(([lado, quem]) => lado.titulares.map((j, i) => {
          if (j.x == null || j.y == null) return null
          // y do time (próprio gol → adversário) vira o comprimento; x (esquerda → direita do time) vira a largura
          // Goleiro colado no gol, para o nome dos zagueiros não passar por cima dele
          const profundidade = j.y <= 5 ? 3.4 : 4.5 + j.y * 0.47
          const fx = quem === 'casa' ? profundidade : C - profundidade
          const fy = quem === 'casa' ? 4 + (j.x / 100) * 60 : L - 4 - (j.x / 100) * 60
          const d = j.id != null ? notas.get(j.id) : undefined
          const ativo = d != null
          return (
            <g key={`${quem}-${i}`} transform={`translate(${fx} ${fy})`}
              className={`atleta-campo ${quem} ${ativo ? 'clicavel' : ''} ${d && selecionado === d.atletaId ? 'foco' : ''}`}
              onClick={() => d && onSelecionar(d.atletaId)}
              onKeyDown={(e) => { if (d && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); onSelecionar(d.atletaId) } }}
              tabIndex={ativo ? 0 : -1} role={ativo ? 'button' : undefined}>
              <title>{`${j.nome}${j.funcao ? ` · ${j.funcao}` : ''}${d?.nota != null ? ` · nota ${d.nota}` : ''}`}</title>
              <circle r={3} />
              <text className="nota-campo" dy={0.95}>{d?.nota ?? '–'}</text>
              <text className="nome-campo" dy={5.6}>{curto(j.nome)}</text>
            </g>
          )
        }))}
      </svg>
      </div>
      <p className="legenda">Posição no desenho tático (não o deslocamento em jogo). No círculo, a nota do jogo de 0 a 100. Toque num atleta para ver o desempenho.</p>
    </div>
  )
}

/** Gols, cartões e trocas ao longo do jogo: mandante acima da linha, visitante abaixo. */
export function LinhaDoTempo({ a }: { a: AnalisePartida }) {
  const eventos = a.eventos.filter((e) => ICONE[e.tipo])
  if (eventos.length === 0) return null
  const minuto = (e: EventoDaPartida) => e.minuto + (e.acrescimo ?? 0) / 4
  const fim = Math.max(90, ...eventos.map((e) => e.minuto)) + 3
  const W = 1000
  const X = (m: number) => 20 + (m / fim) * (W - 40)
  // Lances muito próximos do mesmo time empilham em vez de se sobrepor
  const empilhar = new Map<string, number>()
  const nivel = (e: EventoDaPartida) => {
    const chave = `${e.clubeId}-${Math.round(minuto(e) / 3)}`
    const n = empilhar.get(chave) ?? 0
    empilhar.set(chave, n + 1)
    return Math.min(n, 3) // no máximo 4 por pilha; o resto fica por cima (o detalhe está na lista abaixo)
  }
  const marcas = [0, 15, 30, 45, 60, 75, 90, ...(fim > 100 ? [105, 120] : [])].filter((m) => m <= fim)

  return (
    <div className="linha-tempo-bloco">
      <div className="campo-partida-times">
        <span className="time-casa">{a.mandante.time} ↑</span>
        <span className="time-fora">{a.visitante.time} ↓</span>
      </div>
      <svg viewBox={`0 0 ${W} 236`} className="linha-tempo" role="img" aria-label="Linha do tempo do jogo">
        <line x1={20} y1={105} x2={W - 20} y2={105} className="eixo" />
        {marcas.map((m) => (
          <g key={m}>
            <line x1={X(m)} y1={100} x2={X(m)} y2={110} className="eixo" />
            <text x={X(m)} y={126} className="marca-tempo">{m === 45 ? 'Int.' : `${m}'`}</text>
          </g>
        ))}
        {eventos.map((e, i) => {
          const casa = e.clubeId === a.partida.mandanteId
          const n = nivel(e)
          const y = casa ? 90 - n * 24 : 152 + n * 24
          const quem = e.tipo === 'Substituição' ? `sai ${e.jogador ?? '?'}, entra ${e.outro ?? '?'}` : `${e.jogador ?? ''}${e.outro ? ` (assist. ${e.outro})` : ''}`
          return (
            <g key={i} className={`evento ${casa ? 'casa' : 'fora'} ${e.tipo === 'Substituição' ? 'troca' : ''}`}>
              <title>{`${e.minuto}${e.acrescimo ? `+${e.acrescimo}` : ''}' · ${e.tipo} · ${quem}`}</title>
              <line x1={X(minuto(e))} y1={casa ? y + 6 : y - 18} x2={X(minuto(e))} y2={105} className="haste" />
              <text x={X(minuto(e))} y={y} className="icone-tempo">{ICONE[e.tipo]}</text>
            </g>
          )
        })}
      </svg>
      <p className="legenda">Passe o mouse (ou toque) num ícone para ver o lance. ⚽ gol · 🟨 🟥 cartões · ⇄ substituição.</p>
    </div>
  )
}

/** Atletas do jogo com nota, do melhor para o pior: atalho para abrir o desempenho. */
export function Destaques({ desempenhos, timeDe, selecionado, onSelecionar }: { desempenhos: DesempenhoNoJogo[]; timeDe: (d: DesempenhoNoJogo) => 'casa' | 'fora' } & Selecao) {
  const melhores = desempenhos.filter((d) => d.nota != null).slice(0, 5)
  if (melhores.length === 0) return null
  return (
    <div className="destaques-jogo">
      {melhores.map((d) => (
        <button key={d.atletaId} className={`destaque ${timeDe(d)} ${selecionado === d.atletaId ? 'foco' : ''}`} onClick={() => onSelecionar(d.atletaId)}>
          <b>{d.nota}</b><span>{d.nome}</span>
        </button>
      ))}
    </div>
  )
}

function Fatos({ d }: { d: DesempenhoNoJogo }) {
  return (
    <div className="fatos">
      <span>{d.minutos} min</span>
      {d.gols > 0 && <span>{d.gols} {d.gols === 1 ? 'gol' : 'gols'}</span>}
      {d.assistencias > 0 && <span>{d.assistencias} {d.assistencias === 1 ? 'assistência' : 'assistências'}</span>}
      {d.amarelos > 0 && <span>🟨 {d.amarelos}</span>}
      {d.vermelhos > 0 && <span>🟥 {d.vermelhos}</span>}
    </div>
  )
}

/** Desempenho de um atleta no jogo, com a opção de comparar com outro atleta da mesma partida. */
export function DesempenhoNaPartida({ d, todos, timeNome, timeDe, onFechar }: {
  d: DesempenhoNoJogo; todos: DesempenhoNoJogo[]; timeNome: (d: DesempenhoNoJogo) => string; timeDe: (d: DesempenhoNoJogo) => 'casa' | 'fora'; onFechar: () => void
}) {
  const [outroId, setOutroId] = useState<number | null>(null)
  const outro = todos.find((x) => x.atletaId === outroId && x.atletaId !== d.atletaId) ?? null
  const doSetor = d.indicadores.filter((i) => i.doSetor)
  const demais = d.indicadores.filter((i) => !i.doSetor)

  return (
    <section className="desempenho-jogo" aria-label={`Desempenho de ${d.nome} no jogo`}>
      <div className="card-topo">
        <div className="card-id">
          <div className="card-nome">{d.nome}</div>
          <div className="card-sub">{timeNome(d)}{d.funcao ? ` · ${d.funcao}` : ''}</div>
        </div>
        <div className="nota"><b>{d.nota ?? '–'}</b><span>nota do jogo</span></div>
        <button className="btn fechar-mini" onClick={onFechar} aria-label="Fechar o desempenho">✕</button>
      </div>
      <Fatos d={d} />
      {d.nota == null && <p className="card-sub">Jogou pouco para receber nota (mínimo de 30 minutos). Os percentis abaixo são só referência.</p>}

      <label className="comparar">
        Comparar com
        <select value={outroId ?? ''} onChange={(e) => setOutroId(e.target.value ? Number(e.target.value) : null)}>
          <option value="">ninguém</option>
          {todos.filter((x) => x.atletaId !== d.atletaId).map((x) => (
            <option key={x.atletaId} value={x.atletaId}>{x.nome} · {timeNome(x)}{x.nota != null ? ` · nota ${x.nota}` : ''}</option>
          ))}
        </select>
      </label>

      {outro ? <Comparacao a={d} b={outro} timeNome={timeNome} timeDe={timeDe} /> : (
        <>
          <div className="medidores">
            {doSetor.map((i) => <Medidor key={i.chave} nome={i.nome} valor={i.percentil ?? 0} mediana />)}
          </div>
          {demais.length > 0 && (
            <details>
              <summary>Outros indicadores</summary>
              <div className="medidores">{demais.map((i) => <Medidor key={i.chave} nome={i.nome} valor={i.percentil ?? 0} mediana />)}</div>
            </details>
          )}
        </>
      )}
      <p className="legenda">Percentil = como o rendimento do jogo, levado a 90 minutos, se compara aos atletas do mesmo setor na temporada. A linha no meio marca o atleta mediano.</p>
    </section>
  )
}

/** Dois atletas lado a lado nos indicadores do setor de cada um. */
/** Cor de cada atleta = cor do time dele; dois do mesmo time: o segundo fica num tom mais claro. */
function Comparacao({ a, b, timeNome, timeDe }: { a: DesempenhoNoJogo; b: DesempenhoNoJogo; timeNome: (d: DesempenhoNoJogo) => string; timeDe: (d: DesempenhoNoJogo) => 'casa' | 'fora' }) {
  const corA = timeDe(a)
  const corB = `${timeDe(b)}${timeDe(b) === corA ? ' claro' : ''}`
  const chaves = [...new Set([...a.indicadores, ...b.indicadores].filter((i) => i.doSetor).map((i) => i.chave))]
  const achar = (d: DesempenhoNoJogo, k: string): IndicadorNoJogo | undefined => d.indicadores.find((i) => i.chave === k)
  return (
    <div className="comparacao-jogo">
      <div className="comparacao-topo">
        <div className={corA}><b>{a.nome}</b><span className="card-sub">{timeNome(a)} · nota {a.nota ?? '–'}</span></div>
        <div className={corB}><b>{b.nome}</b><span className="card-sub">{timeNome(b)} · nota {b.nota ?? '–'}</span></div>
      </div>
      {a.setor !== b.setor && <p className="card-sub">Setores diferentes: cada um é comparado com o próprio setor.</p>}
      {chaves.map((k) => {
        const ia = achar(a, k), ib = achar(b, k)
        const nome = (ia ?? ib)!.nome
        return (
          <div key={k} className="comparacao-linha">
            <span className="comparacao-nome">{nome}</span>
            <Barra valor={ia?.percentil} cor={corA} />
            <Barra valor={ib?.percentil} cor={corB} />
          </div>
        )
      })}
    </div>
  )
}

function Barra({ valor, cor }: { valor?: number | null; cor: string }) {
  return (
    <div className={`barra-comp ${cor}`} title={valor == null ? 'Sem dado' : `Percentil ${valor}`}>
      <div className="trilho"><i style={{ width: `${Math.max(2, valor ?? 0)}%` }} /></div>
      <b>{valor ?? '–'}</b>
    </div>
  )
}
