import { useState } from 'react'
import type { DetalheAtleta } from './api'

type Ponto = DetalheAtleta['posicoes'][number]

// Campo em pé (68 x 105 m): o time ataca para cima, a esquerda do time fica à esquerda da tela.
const L = 68
const C = 105

/** Onde o atleta começou os jogos no desenho tático. Círculo maior = mais jogos naquela posição. */
export function CampoDePosicoes({ posicoes }: { posicoes: Ponto[] }) {
  const [foco, setFoco] = useState<Ponto | null>(null)
  if (posicoes.length === 0) return <p className="card-sub">Sem escalações registradas nesta competição.</p>
  const max = Math.max(...posicoes.map((p) => p.jogos))
  const total = posicoes.reduce((s, p) => s + p.jogos, 0)
  const raio = (j: number) => 1.8 + 3.4 * Math.sqrt(j / max)
  const principais = [...posicoes].sort((a, b) => b.jogos - a.jogos).slice(0, 2)
  const descrever = (p: Ponto) => `${p.funcao}: ${p.jogos} ${p.jogos === 1 ? 'jogo' : 'jogos'} (${Math.round((p.jogos / total) * 100)}% das escalações)`

  return (
    <div className="campo-bloco">
      <svg viewBox={`-2 -2 ${L + 4} ${C + 4}`} className="campo" role="img" aria-label={`Posições: ${posicoes.map(descrever).join('; ')}`}>
        <rect x={0} y={0} width={L} height={C} rx={1.5} className="gramado" />
        <g className="linhas">
          <rect x={0} y={0} width={L} height={C} rx={1.5} />
          <line x1={0} y1={C / 2} x2={L} y2={C / 2} />
          <circle cx={L / 2} cy={C / 2} r={9.15} />
          <rect x={L / 2 - 20.16} y={0} width={40.32} height={16.5} />
          <rect x={L / 2 - 9.16} y={0} width={18.32} height={5.5} />
          <rect x={L / 2 - 20.16} y={C - 16.5} width={40.32} height={16.5} />
          <rect x={L / 2 - 9.16} y={C - 5.5} width={18.32} height={5.5} />
        </g>
        {posicoes.map((p) => (
          <circle
            key={`${p.x}-${p.y}`}
            cx={(p.x / 100) * L}
            cy={C - (p.y / 100) * C}
            r={raio(p.jogos)}
            className={`ponto-campo ${foco === p ? 'foco' : ''}`}
            onMouseEnter={() => setFoco(p)}
            onMouseLeave={() => setFoco(null)}
          >
            <title>{descrever(p)}</title>
          </circle>
        ))}
      </svg>
      <div className="campo-info">
        <p className="legenda">Posição de início no esquema de cada jogo como titular. Círculo maior = mais jogos ali. O time ataca para cima.</p>
        <ul className="campo-lista">
          {principais.map((p) => (
            <li key={`${p.x}-${p.y}`}><b>{p.funcao}</b> <span className="card-sub">{p.jogos} jogos · {Math.round((p.jogos / total) * 100)}%</span></li>
          ))}
        </ul>
        <p className="lt-dica" aria-live="polite">{foco ? descrever(foco) : 'Passe o mouse num círculo para ver o detalhe.'}</p>
      </div>
    </div>
  )
}

/** Barra divergente: o centro é o equilíbrio; esquerda = mais marcador, direita = mais ofensivo. */
export function BarraDeEstilo({ estilo }: { estilo: NonNullable<DetalheAtleta['estilo']> }) {
  const metade = Math.min(50, Math.abs(estilo.indice) / 2)
  const ofensivo = estilo.indice >= 0
  return (
    <div className="estilo">
      <div className="estilo-topo">
        <b>{estilo.rotulo}</b>
        <span className="card-sub">Ataque {estilo.ofensivo} · Marcação {estilo.defensivo}</span>
      </div>
      <div className="estilo-trilho" role="img" aria-label={`${estilo.rotulo}: índice ${estilo.indice} de −100 a 100`}>
        <i className={ofensivo ? 'of' : 'def'} style={ofensivo ? { left: '50%', width: `${metade}%` } : { right: '50%', width: `${metade}%` }} />
        <em aria-hidden />
      </div>
      <div className="estilo-eixo"><span>mais marcador</span><span>equilibrado</span><span>mais ofensivo</span></div>
      <p className="legenda">{estilo.explicacao}</p>
    </div>
  )
}
