import { useEffect, useMemo, useState } from 'react'
import { api, type AnalisePartida, type DesempenhoNoJogo, type EventoDaPartida, type LadoDaPartida, type MapaDaPartida, type ResumoPartida, type RoteiroPosJogo } from './api'
import { GaleriaVideo } from './GaleriaVideo'
import { PranchetaTatica } from './Prancheta'
import { cartaoDoJogador, cartoesDaPartida, coletar, ligarJogador, recortesDoMapa, type Cartao } from './ParaVideo'
import { Escudo } from './PainelTime'
import { CampoDaPartida, DesempenhoNaPartida, Destaques, LinhaDoTempo } from './PartidaVisual'

const ICONE: Record<string, string> = {
  'Gol': '⚽', 'Gol de pênalti': '⚽', 'Gol contra': '⚽', 'Pênalti perdido': '✕',
  'Pênalti convertido na disputa': '⚽', 'Pênalti perdido na disputa': '✕',
  'Cartão amarelo': '🟨', 'Cartão vermelho': '🟥', 'Substituição': '⇄', 'VAR': 'VAR',
}

function dataDoJogo(p: ResumoPartida) {
  return new Date(p.data).toLocaleDateString('pt-BR', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' })
}

function Placar({ p, grande = false }: { p: ResumoPartida; grande?: boolean }) {
  return (
    <div className={`placar ${grande ? 'grande' : ''}`}>
      <div className="placar-time">
        <Escudo time={{ nome: p.mandante, escudo: p.escudoMandante }} />
        <span>{p.mandante}</span>
      </div>
      <b>{p.golsMandante ?? '–'} <small>x</small> {p.golsVisitante ?? '–'}</b>
      <div className="placar-time fora">
        <span>{p.visitante}</span>
        <Escudo time={{ nome: p.visitante, escudo: p.escudoVisitante }} />
      </div>
    </div>
  )
}

export function CartaoPartida({ partida: p, onAbrir }: { partida: ResumoPartida; gravacao: boolean; onAbrir: () => void }) {
  return (
    <button className="card" onClick={onAbrir}>
      <span className="card-sub">{p.competicao} {p.temporada}{p.rodada ? ` · ${p.rodada.replace('Regular Season - ', 'Rodada ')}` : ''} · {dataDoJogo(p)}</span>
      <Placar p={p} />
    </button>
  )
}

type PainelProps = { partida: ResumoPartida; gravacao: boolean; onFechar: () => void }

export function PainelPartida({ partida, gravacao, onFechar }: PainelProps) {
  const [a, setA] = useState<AnalisePartida | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [selecionado, setSelecionado] = useState<number | null>(null)

  useEffect(() => {
    setA(null); setErro(null); setSelecionado(null)
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
  const desempenhos = a?.desempenhos ?? []
  const notas = useMemo(() => new Map((a?.desempenhos ?? []).map((d) => [d.atletaId, d])), [a])
  const titulares = new Set([...(a?.mandante.titulares ?? []), ...(a?.visitante.titulares ?? [])].map((j) => j.id))
  const entraram = desempenhos.filter((d) => !titulares.has(d.atletaId))
  const timeDe = (d: DesempenhoNoJogo) => (d.clubeId === p.mandanteId ? 'casa' : 'fora') as 'casa' | 'fora'
  const timeNome = (d: DesempenhoNoJogo) => (d.clubeId === p.mandanteId ? p.mandante : p.visitante)
  const aberto = selecionado != null ? notas.get(selecionado) : undefined
  const [prancheta, setPrancheta] = useState<{ id: number; nome: string } | null>(null)

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel largo" role="dialog" aria-modal aria-label={`${p.mandante} x ${p.visitante}`} onClick={(e) => e.stopPropagation()}>
        <span className="card-sub">{p.competicao} {p.temporada}{p.rodada ? ` · ${p.rodada.replace('Regular Season - ', 'Rodada ')}` : ''} · {dataDoJogo(p)}</span>
        <Placar p={p} grande />
        {a?.desfecho && <p className="desfecho">{a.desfecho}</p>}
        <div className="prancheta-botoes">
          <button className="btn" onClick={() => setPrancheta({ id: p.mandanteId, nome: p.mandante })}>🧩 Prancheta: {p.mandante}</button>
          <button className="btn" onClick={() => setPrancheta({ id: p.visitanteId, nome: p.visitante })}>🧩 Prancheta: {p.visitante}</button>
        </div>
        {prancheta && <PranchetaTatica clubeId={prancheta.id} time={prancheta.nome} onFechar={() => setPrancheta(null)} />}

        {erro && <p className="erro">{erro}</p>}
        {a && !gravacao && p.golsMandante != null && <PosJogo a={a} />}
        {!a && !erro && <p className="pensando">Carregando a partida…</p>}

        {a && (
          <>
            {(desempenhos.length > 0 || a.mandante.titulares.some((j) => j.x != null)) && (
              <section>
                <h3>Em campo</h3>
                <Destaques desempenhos={desempenhos} timeDe={timeDe} selecionado={selecionado} onSelecionar={setSelecionado} />
                <CampoDaPartida a={a} notas={notas} selecionado={selecionado} onSelecionar={setSelecionado} />
                {entraram.length > 0 && (
                  <div className="entraram">
                    <span className="card-sub">Entraram:</span>
                    {entraram.map((d) => (
                      <button key={d.atletaId} className={`destaque ${timeDe(d)} ${selecionado === d.atletaId ? 'foco' : ''}`} onClick={() => setSelecionado(d.atletaId)}>
                        <b>{d.nota ?? '–'}</b><span>{d.nome}</span>
                      </button>
                    ))}
                  </div>
                )}
                {aberto && <DesempenhoNaPartida key={aberto.atletaId} d={aberto} todos={desempenhos} timeNome={timeNome} timeDe={timeDe} onFechar={() => setSelecionado(null)} />}
                {aberto && !gravacao && p.golsMandante != null && <MapaDoJogador key={`mapa-${aberto.atletaId}`} a={a} d={aberto} />}
              </section>
            )}

            {p.golsMandante != null && <NoCampo a={a} />}

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
                <LinhaDoTempo a={a} />
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

            {a.confronto && (
              <section>
                <h3>Confronto direto</h3>
                <div className="confronto">
                  <span><b>{a.confronto.vitoriasMandante}</b> vitórias {p.mandante}</span>
                  <span><b>{a.confronto.empates}</b> empates</span>
                  <span><b>{a.confronto.vitoriasVisitante}</b> vitórias {p.visitante}</span>
                </div>
                <p className="legenda">Jogos entre os dois que estão na base{a.confronto.desdeAno ? ` (desde ${a.confronto.desdeAno})` : ''}, em todas as competições importadas. Decisão por pênaltis conta como empate.</p>
                <div className="lista-jogos">
                  {a.confronto.ultimos.map((j) => <CartaoPartida key={j.id} partida={j} gravacao={gravacao} onAbrir={() => {}} />)}
                </div>
              </section>
            )}

            {(a.jogosSeguintes?.length ?? 0) > 0 && (
              <section>
                <h3>Jogo seguinte</h3>
                <div className="lista-jogos">
                  {a.jogosSeguintes!.map((j) => <CartaoPartida key={j.id} partida={j} gravacao={gravacao} onAbrir={() => {}} />)}
                </div>
              </section>
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

/** Pós-jogo do canal: o roteiro do Short (a IA escreve na hora ou já veio do automático) e os cartões "Para vídeo". */
function PosJogo({ a }: { a: AnalisePartida }) {
  const [roteiro, setRoteiro] = useState<RoteiroPosJogo | null>(null)
  const [estado, setEstado] = useState<'' | 'escrevendo' | 'baixando' | 'copiado'>('')
  const [erro, setErro] = useState<string | null>(null)
  const id = a.partida.id

  useEffect(() => { setRoteiro(null); api.posJogo(id).then(setRoteiro).catch(() => {}) }, [id])

  async function escrever() {
    setEstado('escrevendo'); setErro(null)
    try { setRoteiro(await api.gerarPosJogo(id)) } catch (e) { setErro((e as Error).message) } finally { setEstado('') }
  }
  const [galeria, setGaleria] = useState<Cartao[] | null>(null)
  async function cartoes() {
    setEstado('baixando')
    // o mapa (finalizações e calor) vem de outra fonte e pode não existir (copas, jogo recente): sem ele, saem os 3 primeiros cartões
    try { setGaleria(await coletar(async () => cartoesDaPartida(a, await mapaDe(id)))) } finally { setEstado('') }
  }
  async function copiar() {
    if (!roteiro) return
    try { await navigator.clipboard.writeText(roteiro.texto); setEstado('copiado'); setTimeout(() => setEstado(''), 1500) } catch { /* sem permissão: o texto continua na tela */ }
  }

  return (
    <section className="pos-jogo">
      <h3>Pós-jogo do canal</h3>
      <div className="pos-jogo-acoes">
        <button className="btn" onClick={escrever} disabled={estado === 'escrevendo'}>
          {estado === 'escrevendo' ? 'Escrevendo…' : roteiro ? 'Escrever de novo' : 'Escrever roteiro'}
        </button>
        <button className="btn" onClick={cartoes} disabled={estado === 'baixando'} title="Mostra os cartões 1080x1920 (placar, onde o jogo foi decidido, destaques e, nas ligas, o mapa) para ver grande, gravar ou baixar">
          {estado === 'baixando' ? 'Gerando…' : '🎬 Para vídeo'}
        </button>
        {roteiro && <button className="btn" onClick={copiar}>{estado === 'copiado' ? 'Copiado!' : 'Copiar roteiro'}</button>}
      </div>
      {erro && <p className="erro">{erro}</p>}
      {roteiro && <pre className="roteiro">{roteiro.texto}</pre>}
      {galeria && <GaleriaVideo titulo={`${a.partida.mandante} x ${a.partida.visitante}: para vídeo`} cartoes={galeria} onFechar={() => setGaleria(null)} />}
    </section>
  )
}

/** No campo: onde cada time finalizou e onde o time do canal teve a bola, na própria análise (sem gerar imagem para baixar). */
function NoCampo({ a }: { a: AnalisePartida }) {
  const [recortes, setRecortes] = useState<{ finalizacoes: string; calor: string | null } | null>(null)
  useEffect(() => {
    let vivo = true
    mapaDe(a.partida.id).then(async (m) => { if (m && vivo) setRecortes(await recortesDoMapa(a, m)) })
    return () => { vivo = false }
  }, [a])
  if (!recortes) return null
  return (
    <section>
      <h3>No campo</h3>
      <p className="legenda">Onde cada time finalizou (bolinha maior = chance mais clara) e onde o time teve a bola, do jogo inteiro.</p>
      <div className="no-campo">
        <img src={recortes.finalizacoes} alt="Mapa de finalizações" />
        {recortes.calor && <img src={recortes.calor} alt="Mapa de calor" />}
      </div>
    </section>
  )
}

// O mapa vem de outra fonte: busca uma vez por partida (cada jogador aberto reaproveita)
const mapas = new Map<number, Promise<MapaDaPartida | null>>()
const mapaDe = (id: number) => {
  if (!mapas.has(id)) mapas.set(id, api.mapa(id).catch(() => null))
  return mapas.get(id)!
}

/**
 * "Mapa de <nome>": o cartão com a mancha de calor e as finalizações do jogador aberto no campinho, na tela (ver grande, gravar ou
 * baixar). Só aparece quando a fonte do mapa tem o jogo e o jogador (copas e jogos ainda não processados não têm).
 */
function MapaDoJogador({ a, d }: { a: AnalisePartida; d: DesempenhoNoJogo }) {
  const [mapa, setMapa] = useState<MapaDaPartida | null | undefined>(undefined)
  const [gerando, setGerando] = useState(false)
  const [galeria, setGaleria] = useState<Cartao[] | null>(null)
  useEffect(() => {
    let vivo = true
    mapaDe(a.partida.id).then((m) => { if (vivo) setMapa(m) })
    return () => { vivo = false }
  }, [a.partida.id])
  if (!mapa || !ligarJogador(d.nome, d.clubeId, mapa)) return null
  async function mostrar() {
    setGerando(true)
    try {
      const cartoes = await coletar(() => cartaoDoJogador(a, mapa!, d))
      if (cartoes.length) setGaleria(cartoes)
    } finally { setGerando(false) }
  }
  return (
    <div className="pos-jogo-acoes">
      {galeria && <GaleriaVideo titulo={`Mapa de ${d.nome}`} cartoes={galeria} onFechar={() => setGaleria(null)} />}
      <button className="btn para-video" onClick={mostrar} disabled={gerando} title="Mostra o cartão 1080x1920 com onde ele teve a bola e as finalizações dele">
        {gerando ? 'Gerando…' : `🎬 Mapa de ${d.nome}`}
      </button>
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
