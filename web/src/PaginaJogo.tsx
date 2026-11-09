import { useEffect, useState } from 'react'
import type { AnalisePartida, Palpite, ResumoPartida } from './api'
import { coresDoJogo } from './Cores'
import { corDaNota } from './ParaVideo'

/**
 * Página pública de um jogo (backlog item 5): /jogo/<id>, sem login, para o link da bio e da descrição dos vídeos do canal
 * Futebol em Números. Placar, gols, onde o jogo foi decidido, destaques pela nota e o palpite do modelo ("acertou ou errou?").
 * Sem fotos de atleta; só escudos, no visual dos cartões do canal.
 */
type Pagina = {
  partida: ResumoPartida
  desfecho: string | null
  gols: { minuto: string; time: string; jogador: string; assistencia: string | null; detalhe: string | null }[]
  aspectos: AnalisePartida['aspectos']
  destaques: { nome: string; time: string; funcao: string | null; nota: number; gols: number; assistencias: number }[]
  palpite: Palpite | null
  explicacao: string
}

const CANAL = 'https://youtube.com/@leandroborgesscout'

export function idDaPaginaPublica(caminho: string): number | null {
  const m = caminho.match(/^\/jogo\/(\d+)\/?$/)
  return m ? Number(m[1]) : null
}

export function PaginaJogo({ id }: { id: number }) {
  const [pagina, setPagina] = useState<Pagina | null | undefined>(undefined)

  useEffect(() => {
    fetch(`/api/publico/jogos/${id}`)
      .then((r) => (r.ok ? (r.json() as Promise<Pagina>) : null))
      .then((p) => {
        setPagina(p)
        if (p) document.title = `${p.partida.mandante} x ${p.partida.visitante} · Futebol em Números`
      })
      .catch(() => setPagina(null))
  }, [id])

  if (pagina === undefined) return <div className="pj"><p className="pj-aviso">Carregando o jogo…</p></div>
  if (pagina === null)
    return (
      <div className="pj">
        <Marca />
        <p className="pj-aviso">Este jogo ainda não tem análise publicada.</p>
        <Chamada />
      </div>
    )

  const p = pagina.partida
  const [corM, corV] = coresDoJogo(p.mandante, p.visitante)
  const encerrado = p.golsMandante != null
  const data = new Date(p.data).toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })

  return (
    <div className="pj">
      <Marca />
      <header className="pj-placar">
        <span className="pj-selo">{[p.competicao, p.rodada?.replace('Regular Season - ', 'Rodada ')].filter(Boolean).join(' · ')}</span>
        <div className="pj-times">
          <Time nome={p.mandante} escudo={p.escudoMandante} cor={corM} />
          <div className="pj-gols">{encerrado ? `${p.golsMandante} x ${p.golsVisitante}` : 'x'}</div>
          <Time nome={p.visitante} escudo={p.escudoVisitante} cor={corV} />
        </div>
        <span className="pj-data">{data}</span>
        {pagina.desfecho && <span className="pj-data">{pagina.desfecho}</span>}
      </header>

      {pagina.palpite && <PalpitePublico x={pagina.palpite} corM={corM} corV={corV} />}

      {pagina.gols.length > 0 && (
        <section className="pj-bloco">
          <h2>Gols</h2>
          {pagina.gols.map((g, i) => (
            <div key={i} className="pj-gol">
              <b style={{ color: g.time === p.mandante ? corM : corV }}>{g.minuto}</b>
              <span>{g.jogador}{g.detalhe ? ` (${g.detalhe})` : ''}{g.assistencia ? ` · assist. ${g.assistencia}` : ''}</span>
              <small>{g.time}</small>
            </div>
          ))}
        </section>
      )}

      {pagina.aspectos.length > 0 && (
        <section className="pj-bloco">
          <h2>Onde o jogo foi decidido</h2>
          <p className="pj-legenda"><i style={{ background: corM }} /> {p.mandante} <i style={{ background: corV }} /> {p.visitante}</p>
          {pagina.aspectos.map((a) => (
            <div key={a.aspecto} className="pj-aspecto">
              <span>{a.aspecto}</span>
              <Barra valor={a.percentilMandante!} cor={corM} />
              <Barra valor={a.percentilVisitante!} cor={corV} />
            </div>
          ))}
        </section>
      )}

      {pagina.destaques.length > 0 && (
        <section className="pj-bloco">
          <h2>Destaques pela nota da IA</h2>
          {pagina.destaques.map((d) => (
            <div key={d.nome + d.time} className="pj-destaque">
              <b style={{ background: corDaNota(d.nota) }}>{d.nota}</b>
              <div>
                <strong>{d.nome}</strong>
                <small>{[d.time, d.funcao, d.gols ? `${d.gols} gol${d.gols > 1 ? 's' : ''}` : '', d.assistencias ? `${d.assistencias} assist.` : ''].filter(Boolean).join(' · ')}</small>
              </div>
            </div>
          ))}
        </section>
      )}

      {encerrado && <p className="pj-legenda">{pagina.explicacao}</p>}
      <Chamada />
    </div>
  )
}

function Marca() {
  return (
    <a className="pj-marca" href={CANAL} target="_blank" rel="noreferrer">
      Futebol em <span>Números</span>
    </a>
  )
}

function Chamada() {
  return (
    <footer className="pj-chamada">
      <a className="pj-botao" href={CANAL} target="_blank" rel="noreferrer">🔔 Se inscreve no Futebol em Números</a>
      <small>Análises com a IA do AlpaFlow Scout · scout.alpaflow.tec.br</small>
    </footer>
  )
}

function Time({ nome, escudo, cor }: { nome: string; escudo?: string | null; cor: string }) {
  return (
    <div className="pj-time">
      {escudo ? <img src={escudo} alt="" width={56} height={56} /> : <span className="pj-sem-escudo" style={{ borderColor: cor }} />}
      <span>{nome}</span>
    </div>
  )
}

function Barra({ valor, cor }: { valor: number; cor: string }) {
  return (
    <div className="pj-barra">
      <i style={{ width: `${Math.max(3, valor)}%`, background: cor }} />
      <b>{Math.round(valor)}</b>
    </div>
  )
}

function PalpitePublico({ x, corM, corV }: { x: Palpite; corM: string; corV: string }) {
  const p = x.partida
  const faixas = [
    { nome: p.mandante, valor: x.chanceMandante, cor: corM, chave: 'mandante' },
    { nome: 'Empate', valor: x.chanceEmpate, cor: '#9aa5a0', chave: 'empate' },
    { nome: p.visitante, valor: x.chanceVisitante, cor: corV, chave: 'visitante' },
  ]
  return (
    <section className="pj-bloco">
      <h2>
        Palpite do modelo
        {x.acertou != null && <span className={`pj-resultado ${x.acertou ? 'certo' : 'errado'}`}>{x.acertou ? '✅ Acertou' : '❌ Errou'}</span>}
      </h2>
      {faixas.map((f) => (
        <div key={f.chave} className={`pj-faixa ${x.favorito === f.chave ? 'favorito' : ''}`}>
          <span>{f.nome}{x.resultado === f.chave ? ' ⚽' : ''}</span>
          <Barra valor={f.valor} cor={f.cor} />
        </div>
      ))}
      <p className="pj-legenda">
        Placar mais provável: <b>{x.placarProvavel}</b> · {x.congelado ? 'palpite guardado antes do apito inicial' : 'atualizado até o apito inicial'}.
        Modelo AlpaFlow: força tipo Elo pelos resultados da temporada, com mando de campo.
      </p>
    </section>
  )
}
