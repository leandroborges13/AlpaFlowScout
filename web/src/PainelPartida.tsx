import { useEffect, useState } from 'react'
import { api, type AnalisePartida, type EventoDaPartida, type LadoDaPartida, type ResumoPartida } from './api'
import { Escudo } from './PainelTime'

const ICONE: Record<string, string> = {
  'Gol': '⚽', 'Gol de pênalti': '⚽', 'Gol contra': '⚽', 'Pênalti perdido': '✕',
  'Cartão amarelo': '🟨', 'Cartão vermelho': '🟥', 'Substituição': '⇄', 'VAR': 'VAR',
}

function dataDoJogo(p: ResumoPartida) {
  return new Date(p.data).toLocaleDateString('pt-BR', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' })
}

function Placar({ p, gravacao, grande = false }: { p: ResumoPartida; gravacao: boolean; grande?: boolean }) {
  return (
    <div className={`placar ${grande ? 'grande' : ''}`}>
      <div className="placar-time">
        <Escudo time={{ nome: p.mandante, escudo: p.escudoMandante }} oculto={gravacao} />
        <span>{p.mandante}</span>
      </div>
      <b>{p.golsMandante ?? '–'} <small>x</small> {p.golsVisitante ?? '–'}</b>
      <div className="placar-time fora">
        <span>{p.visitante}</span>
        <Escudo time={{ nome: p.visitante, escudo: p.escudoVisitante }} oculto={gravacao} />
      </div>
    </div>
  )
}

export function CartaoPartida({ partida: p, gravacao, onAbrir }: { partida: ResumoPartida; gravacao: boolean; onAbrir: () => void }) {
  return (
    <button className="card" onClick={onAbrir}>
      <span className="card-sub">{p.competicao} {p.temporada}{p.rodada ? ` · ${p.rodada.replace('Regular Season - ', 'Rodada ')}` : ''} · {dataDoJogo(p)}</span>
      <Placar p={p} gravacao={gravacao} />
    </button>
  )
}

type PainelProps = { partida: ResumoPartida; gravacao: boolean; onFechar: () => void }

export function PainelPartida({ partida, gravacao, onFechar }: PainelProps) {
  const [a, setA] = useState<AnalisePartida | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setA(null); setErro(null)
    api.partida(partida.id).then(setA).catch((e) => setErro(e.message))
  }, [partida])

  useEffect(() => {
    const fechar = (e: KeyboardEvent) => { if (e.key === 'Escape') onFechar() }
    window.addEventListener('keydown', fechar)
    return () => window.removeEventListener('keydown', fechar)
  }, [onFechar])

  const p = a?.partida ?? partida
  const principais = (a?.eventos ?? []).filter((e) => e.tipo !== 'Substituição')
  const trocas = (a?.eventos ?? []).filter((e) => e.tipo === 'Substituição')

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel" role="dialog" aria-modal aria-label={`${p.mandante} x ${p.visitante}`} onClick={(e) => e.stopPropagation()}>
        <span className="card-sub">{p.competicao} {p.temporada}{p.rodada ? ` · ${p.rodada.replace('Regular Season - ', 'Rodada ')}` : ''} · {dataDoJogo(p)}</span>
        <Placar p={p} gravacao={gravacao} grande />

        {erro && <p className="erro">{erro}</p>}
        {!a && !erro && <p className="pensando">Carregando a partida…</p>}

        {a && (
          <>
            {a.aspectos.length > 0 && (
              <section>
                <h3>Quem levou vantagem</h3>
                <p className="legenda">Em cada aspecto, o percentil mostra como o time foi em relação aos próprios jogos na temporada.</p>
                <ul className="aspectos">
                  {a.aspectos.map((x) => (
                    <li key={x.aspecto}>
                      <b>{x.aspecto}</b>
                      <span className={`vantagem ${x.vantagem === 'Equilibrado' ? 'neutra' : ''}`}>{x.vantagem === 'Equilibrado' ? 'equilibrado' : `vantagem ${x.vantagem}`}</span>
                      <span className="card-sub">
                        {p.mandante} {x.percentilMandante ?? '—'} · {p.visitante} {x.percentilVisitante ?? '—'}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {principais.length > 0 && (
              <section>
                <h3>Lances</h3>
                <ol className="lances">
                  {principais.map((e, i) => <Lance key={i} e={e} p={p} />)}
                </ol>
              </section>
            )}

            <section>
              <h3>Escalações</h3>
              <div className="escalacoes">
                <Escalacao lado={a.mandante} />
                <Escalacao lado={a.visitante} />
              </div>
            </section>

            {trocas.length > 0 && (
              <details className="transferencias">
                <summary>{trocas.length} substituições</summary>
                <ol className="lances">{trocas.map((e, i) => <Lance key={i} e={e} p={p} />)}</ol>
              </details>
            )}

            {a.observacoes.map((o) => <p key={o} className="obs">{o}</p>)}
          </>
        )}

        <div className="acoes">
          <button className="btn prim" onClick={onFechar}>Fechar</button>
        </div>
      </div>
    </div>
  )
}

function Lance({ e, p }: { e: EventoDaPartida; p: ResumoPartida }) {
  const time = e.clubeId === p.mandanteId ? p.mandante : p.visitante
  const texto = e.tipo === 'Substituição'
    ? `sai ${e.jogador ?? '?'}, entra ${e.outro ?? '?'}`
    : `${e.jogador ?? ''}${e.outro ? ` (assistência de ${e.outro})` : ''}`
  return (
    <li>
      <span className="minuto">{e.minuto}{e.acrescimo ? `+${e.acrescimo}` : ''}'</span>
      <span className="icone" aria-hidden>{ICONE[e.tipo] ?? '•'}</span>
      <span><b>{e.tipo}</b> · {time}: {texto}{e.detalhe ? ` · ${e.detalhe}` : ''}</span>
    </li>
  )
}

function Escalacao({ lado }: { lado: LadoDaPartida }) {
  return (
    <div className="escalacao">
      <b>{lado.time}{lado.esquema ? ` · ${lado.esquema}` : ''}</b>
      {lado.treinador && <span className="card-sub">Técnico: {lado.treinador}</span>}
      <ol>
        {lado.titulares.map((j, i) => <li key={i}>{j.nome}{j.funcao ? <span className="card-sub"> · {j.funcao}</span> : null}</li>)}
      </ol>
    </div>
  )
}
