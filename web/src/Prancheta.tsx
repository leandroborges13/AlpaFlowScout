import { useEffect, useMemo, useRef, useState } from 'react'
import { coresDoClube, coresDoJogo, type CoresDoClube } from './Cores'
import { createPortal } from 'react-dom'
import { api, type AtletaDaPrancheta, type Prancheta as Dados } from './api'
import { baixarCartao } from './ParaVideo'

/**
 * Prancheta tática para gravar vídeo: o campo com a escalação provável do esquema (pelos dados do Scout) e o elenco ao lado.
 * Arraste os jogadores no campo; clique num jogador e depois em outro (no campo ou no elenco) para trocar; o nome pode ser
 * digitado (quem não está na base, como os garotos do Sub-20). "Modo grande" ocupa a tela para gravar (16:9 ou 9:16):
 * H esconde os botões, F põe em tela cheia, Esc volta. "Baixar imagem" gera o quadro em PNG.
 * Setas: no modo seta (movimento ou passe), arraste no campo para desenhar; clique numa seta para apagar. Aberta de um jogo, a
 * prancheta mostra também o adversário (escalação provável espelhada, atacando no sentido contrário), para explicar o confronto.
 */
const ESQUEMAS = ['4-3-3', '4-2-3-1', '4-4-2', '4-1-4-1', '4-3-1-2', '3-5-2', '3-4-3', '3-4-2-1', '5-3-2', '4-5-1']
const L = 68
const C = 105
const SETORES: Record<string, string> = { Goleiro: 'Goleiros', Defesa: 'Defensores', Meio: 'Meio-campo', Ataque: 'Ataque' }

type Peca = { grade: string; funcao: string; x: number; y: number; atletaId: number | null; nome: string }
/** Seta em coordenadas do campo (0–100). Movimento: amarela e contínua; passe: branca e tracejada. */
type Seta = { x1: number; y1: number; x2: number; y2: number; tipo: 'movimento' | 'passe' }
type Adversario = { id: number; nome: string }

export function PranchetaTatica(props: { clubeId: number; time: string; adversario?: Adversario; onFechar: () => void }) {
  return createPortal(<Tela {...props} />, document.body)
}

/** Cores do adversário na prancheta: as dele, ou cinza quando ficariam parecidas com as do time (Galo x Botafogo). */
function coresDoRival(time: string, rival: string): CoresDoClube {
  const [, destaque] = coresDoJogo(time, rival)
  return destaque === coresDoClube(rival).destaque ? coresDoClube(rival) : { camisa: '#8a9a92', detalhe: '#ffffff', destaque: '#8a9a92' }
}

function Tela({ clubeId, time, adversario, onFechar }: { clubeId: number; time: string; adversario?: Adversario; onFechar: () => void }) {
  const [esquema, setEsquema] = useState<string | undefined>(undefined)
  const [dados, setDados] = useState<Dados | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [pecas, setPecas] = useState<Peca[]>([])
  const [sel, setSel] = useState<number | null>(null)
  const [titulo, setTitulo] = useState('')
  const [grande, setGrande] = useState(false)
  const [formato, setFormato] = useState<'h' | 'v'>('h')
  const [semBotoes, setSemBotoes] = useState(false)
  const [setas, setSetas] = useState<Seta[]>([])
  const [modoSeta, setModoSeta] = useState<'' | Seta['tipo']>('')
  const [rivais, setRivais] = useState<Peca[]>([])
  const [esquemaRival, setEsquemaRival] = useState<string | null>(null)
  const [mostrarRival, setMostrarRival] = useState(false)
  const palco = useRef<HTMLDivElement>(null)

  // O adversário entra espelhado: o goleiro dele em cima e o ataque dele descendo contra a nossa defesa
  function carregarRival() {
    if (!adversario) return
    api.prancheta(adversario.id).then((d) => {
      setEsquemaRival(d.esquema)
      setRivais(d.vagas.map((v) => ({ grade: `r${v.grade}`, funcao: v.funcao, x: 100 - v.x, y: v.y, atletaId: v.atletaId, nome: v.nome ?? '' })))
    }).catch(() => setMostrarRival(false))
  }
  useEffect(() => { if (mostrarRival && rivais.length === 0) carregarRival() }, [mostrarRival]) // eslint-disable-line react-hooks/exhaustive-deps
  const rivaisVisiveis = mostrarRival ? rivais : []
  const coresRival = adversario ? coresDoRival(time, adversario.nome) : coresDoClube('')

  function carregar(e?: string) {
    setErro(null)
    api.prancheta(clubeId, e).then((d) => {
      setDados(d)
      setEsquema(d.esquema)
      setTitulo((t) => t || `${time}: como deve jogar`)
      setPecas(d.vagas.map((v) => ({ grade: v.grade, funcao: v.funcao, x: v.x, y: 100 - v.y, atletaId: v.atletaId, nome: v.nome ?? '' })))
      setSel(null)
    }).catch((x: Error) => setErro(x.message))
  }
  useEffect(() => { carregar() }, [clubeId]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const tecla = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement)?.tagName === 'INPUT') return
      if (['Escape', 'h', 'H', 'f', 'F'].includes(e.key)) e.stopImmediatePropagation()
      if (e.key === 'Escape') { if (document.fullscreenElement) return; if (grande) setGrande(false); else onFechar() }
      else if (grande && e.key.toLowerCase() === 'h') setSemBotoes((v) => !v)
      else if (grande && e.key.toLowerCase() === 'f') telaCheia()
    }
    window.addEventListener('keydown', tecla, true)
    return () => window.removeEventListener('keydown', tecla, true)
  })

  function telaCheia() {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
    else palco.current?.requestFullscreen().catch(() => {})
  }

  // Clique: seleciona; com um selecionado, clicar em outro troca os dois de lugar (as posições ficam, os jogadores trocam)
  function clicarPeca(i: number) {
    if (sel === null) return setSel(i)
    if (sel === i) return setSel(null)
    setPecas((ps) => {
      const n = [...ps]
      const a = { atletaId: n[sel].atletaId, nome: n[sel].nome }
      n[sel] = { ...n[sel], atletaId: n[i].atletaId, nome: n[i].nome }
      n[i] = { ...n[i], ...a }
      return n
    })
    setSel(null)
  }

  // Do elenco para a vaga selecionada; se ele já está em campo, os dois trocam
  function escolherDoElenco(a: AtletaDaPrancheta) {
    if (sel === null) return
    setPecas((ps) => {
      const n = [...ps]
      const onde = n.findIndex((p) => p.atletaId === a.id)
      if (onde >= 0 && onde !== sel) n[onde] = { ...n[onde], atletaId: n[sel].atletaId, nome: n[sel].nome }
      n[sel] = { ...n[sel], atletaId: a.id, nome: a.nome }
      return n
    })
    setSel(null)
  }

  const emCampo = useMemo(() => new Set(pecas.map((p) => p.atletaId).filter((x) => x !== null)), [pecas])
  const titulo2 = `${esquema ?? ''}${mostrarRival && esquemaRival ? ` x ${esquemaRival}` : ''}`

  const campo = (
    <CampoPrancheta cores={coresDoClube(time)} pecas={pecas} sel={sel} onClicar={clicarPeca}
      onMover={(i, x, y) => setPecas((ps) => ps.map((p, k) => (k === i ? { ...p, x, y } : p)))}
      rivais={rivaisVisiveis} coresRival={coresRival}
      onMoverRival={(i, x, y) => setRivais((ps) => ps.map((p, k) => (k === i ? { ...p, x, y } : p)))}
      setas={setas} modoSeta={modoSeta} onNovaSeta={(s) => setSetas((l) => [...l, s])}
      onApagarSeta={(i) => setSetas((l) => l.filter((_, k) => k !== i))} />
  )
  const botoesSeta = (
    <div className="prancheta-setas">
      <button className={`botao-prancheta ${modoSeta === 'movimento' ? 'ativo' : ''}`} onClick={() => setModoSeta(modoSeta === 'movimento' ? '' : 'movimento')}
        title="Arraste no campo para desenhar o movimento">➜ Movimento</button>
      <button className={`botao-prancheta ${modoSeta === 'passe' ? 'ativo' : ''}`} onClick={() => setModoSeta(modoSeta === 'passe' ? '' : 'passe')}
        title="Arraste no campo para desenhar o passe">⇢ Passe</button>
      {setas.length > 0 && <button className="botao-prancheta" onClick={() => setSetas([])}>Limpar setas</button>}
      {adversario && (
        <label className="botao-prancheta">
          <input type="checkbox" checked={mostrarRival} onChange={(e) => setMostrarRival(e.target.checked)} /> Mostrar o {adversario.nome}
        </label>
      )}
    </div>
  )
  const elenco = dados && (
    <Elenco elenco={dados.elenco} emCampo={emCampo} ativo={sel !== null} onEscolher={escolherDoElenco} />
  )

  if (grande) {
    return (
      <div className="prancheta-palco" ref={palco}>
        <div className={`prancheta-quadro ${formato}`}>
          <div className="pq-topo"><span className="pq-selo">{titulo2}</span><h2>{titulo}</h2></div>
          <div className="pq-corpo">
            <div className="pq-campo">{campo}</div>
            {formato === 'h' && <div className="pq-elenco">{elenco}</div>}
          </div>
          <div className="pq-rodape">Futebol em <b>Números</b></div>
        </div>
        {!semBotoes && (
          <div className="galeria-controles">
            <button className="btn" onClick={() => setFormato(formato === 'h' ? 'v' : 'h')}>{formato === 'h' ? '▯ Vertical (Short)' : '▭ Horizontal'}</button>
            <button className="btn" onClick={telaCheia} title="Tela cheia (F)">⛶ Tela cheia</button>
            <button className="btn" onClick={() => setSemBotoes(true)} title="Esconde os botões para gravar (H mostra de novo)">Esconder botões</button>
            {botoesSeta}
            <button className="btn" onClick={() => baixarImagem(pecas, titulo, titulo2, formato, coresDoClube(time), rivaisVisiveis, coresRival, setas)}>Baixar imagem</button>
            <button className="btn" onClick={() => setGrande(false)}>Voltar</button>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel largo prancheta" role="dialog" aria-modal aria-label={`Prancheta: ${time}`} onClick={(e) => e.stopPropagation()}>
        <div className="galeria-topo">
          <h3>🧩 Prancheta: {time}</h3>
          <div className="pos-jogo-acoes">
            <button className="btn prim" onClick={() => setGrande(true)} disabled={!dados}>🎬 Modo grande (gravar)</button>
            <button className="btn" onClick={onFechar}>Fechar</button>
          </div>
        </div>
        {erro && <p className="erro">{erro}</p>}
        {!dados && !erro && <p className="card-sub">Montando a escalação provável…</p>}
        {dados && (
          <div className="prancheta-corpo">
            <div className="prancheta-campo">{campo}</div>
            <div className="prancheta-lado">
              <label className="prancheta-campo-form">Esquema
                <select value={esquema} onChange={(e) => carregar(e.target.value)}>
                  {[...new Set([...dados.esquemas.map((e) => e.esquema), ...ESQUEMAS])].map((e) => {
                    const usado = dados.esquemas.find((x) => x.esquema === e)
                    return <option key={e} value={e}>{e}{usado ? ` (usado em ${usado.jogos} dos últimos jogos)` : ''}</option>
                  })}
                </select>
              </label>
              <label className="prancheta-campo-form">Título do quadro
                <input value={titulo} onChange={(e) => setTitulo(e.target.value)} maxLength={60} />
              </label>
              <button className="btn" onClick={() => carregar(esquema)}>↺ Voltar à escalação provável</button>
              {botoesSeta}
              {modoSeta && <p className="legenda">Arraste no campo para desenhar {modoSeta === 'passe' ? 'o passe' : 'o movimento'}. Clique numa seta para apagar.</p>}
              {sel !== null && (
                <div className="prancheta-sel">
                  <b>{pecas[sel].funcao}</b>
                  <input value={pecas[sel].nome} placeholder="Nome do jogador" autoFocus
                    onChange={(e) => { const v = e.target.value; setPecas((ps) => ps.map((p, k) => (k === sel ? { ...p, nome: v, atletaId: null } : p))) }} />
                  <span className="card-sub">Clique em outro jogador do campo para trocar os dois, ou em um nome do elenco para colocar aqui.</span>
                </div>
              )}
              {sel === null && <p className="legenda">Arraste os jogadores no campo. Clique em um para trocar, mudar o nome ou pôr alguém do elenco.</p>}
              {elenco}
              <p className="legenda">{dados.explicacao}</p>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function CampoPrancheta({ cores, pecas, sel, onClicar, onMover, rivais, coresRival, onMoverRival, setas, modoSeta, onNovaSeta, onApagarSeta }: {
  cores: CoresDoClube; pecas: Peca[]; sel: number | null; onClicar: (i: number) => void; onMover: (i: number, x: number, y: number) => void
  rivais: Peca[]; coresRival: CoresDoClube; onMoverRival: (i: number, x: number, y: number) => void
  setas: Seta[]; modoSeta: '' | Seta['tipo']; onNovaSeta: (s: Seta) => void; onApagarSeta: (i: number) => void
}) {
  const svg = useRef<SVGSVGElement>(null)
  const arrasto = useRef<{ i: number; rival: boolean; moveu: boolean; x0: number; y0: number } | null>(null)
  const [desenho, setDesenho] = useState<Seta | null>(null)
  const acima = rotulosAcima(pecas)
  const acimaRival = rotulosAcima(rivais)

  function ponto(e: React.PointerEvent) {
    const m = svg.current!.getScreenCTM()!.inverse()
    const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(m)
    return { x: Math.min(100, Math.max(0, (p.x / L) * 100)), y: Math.min(100, Math.max(0, (p.y / C) * 100)) }
  }

  function peca(p: Peca, i: number, rival: boolean) {
    const cx = (p.x / 100) * L, cy = (p.y / 100) * C
    // Com os dois times, o nome do adversário vai sempre em cima e o nosso sempre embaixo: assim não se encavalam
    const emCima = rivais.length > 0 ? rival : (rival ? acimaRival : acima).has(i)
    const ty = emCima ? cy - 4.6 : cy + 6.2
    const c = rival ? coresRival : cores
    return (
      <g key={p.grade} className={`pp ${!rival && sel === i ? 'sel' : ''}`} style={{ cursor: modoSeta ? 'crosshair' : 'grab' }}
        onPointerDown={(e) => {
          if (modoSeta) return // no modo seta, a seta pode começar em cima de um jogador
          e.stopPropagation()
          ;(e.target as Element).setPointerCapture?.(e.pointerId)
          arrasto.current = { i, rival, moveu: false, x0: e.clientX, y0: e.clientY }
        }}>
        <circle cx={cx} cy={cy} r={3.3} fill={!rival && sel === i ? '#ffd23f' : c.camisa} stroke={c.detalhe} strokeWidth={0.7} />
        <text x={cx} y={ty} textAnchor="middle" fontSize={2.5} fontWeight={800} fill="#fff" stroke="#07170f" strokeWidth={0.7}
          paintOrder="stroke" fontFamily="Inter, Arial, sans-serif">{curto(p.nome) || '?'}</text>
      </g>
    )
  }

  return (
    <svg ref={svg} viewBox={`-3 -3 ${L + 6} ${C + 6}`} className="prancheta-svg" role="img" aria-label="Campo com a escalação"
      onPointerDown={(e) => {
        if (!modoSeta) return
        ;(e.currentTarget as Element).setPointerCapture?.(e.pointerId)
        const p = ponto(e)
        setDesenho({ x1: p.x, y1: p.y, x2: p.x, y2: p.y, tipo: modoSeta })
      }}
      onPointerMove={(e) => {
        if (desenho) { const p = ponto(e); setDesenho({ ...desenho, x2: p.x, y2: p.y }); return }
        const a = arrasto.current
        if (!a) return
        if (Math.abs(e.clientX - a.x0) + Math.abs(e.clientY - a.y0) > 4) a.moveu = true
        if (a.moveu) { const p = ponto(e); (a.rival ? onMoverRival : onMover)(a.i, p.x, p.y) }
      }}
      onPointerUp={() => {
        if (desenho) { if (Math.hypot(desenho.x2 - desenho.x1, desenho.y2 - desenho.y1) > 4) onNovaSeta(desenho); setDesenho(null); return }
        const a = arrasto.current; arrasto.current = null
        if (a && !a.moveu && !a.rival) onClicar(a.i)
      }}
      onPointerLeave={() => { arrasto.current = null; setDesenho(null) }}>
      <defs>
        <marker id="ponta-movimento" viewBox="0 0 10 10" refX="7" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="#ffd23f" />
        </marker>
        <marker id="ponta-passe" viewBox="0 0 10 10" refX="7" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="#ffffff" />
        </marker>
      </defs>
      <Gramado />
      {rivais.map((p, i) => peca(p, i, true))}
      {pecas.map((p, i) => peca(p, i, false))}
      {[...setas, ...(desenho ? [desenho] : [])].map((s, i) => (
        <line key={i} x1={(s.x1 / 100) * L} y1={(s.y1 / 100) * C} x2={(s.x2 / 100) * L} y2={(s.y2 / 100) * C}
          stroke={s.tipo === 'passe' ? '#ffffff' : '#ffd23f'} strokeWidth={s.tipo === 'passe' ? 0.6 : 0.85} strokeLinecap="round"
          strokeDasharray={s.tipo === 'passe' ? '1.6 1.2' : undefined} markerEnd={`url(#ponta-${s.tipo})`}
          style={{ cursor: modoSeta ? 'crosshair' : 'pointer', pointerEvents: i < setas.length && !modoSeta ? 'stroke' : 'none' }}
          onClick={() => { if (!modoSeta && i < setas.length) onApagarSeta(i) }} />
      ))}
    </svg>
  )
}

function Gramado() {
  return (
    <g>
      <rect x={-3} y={-3} width={L + 6} height={C + 6} fill="#0b2a1d" />
      {Array.from({ length: 7 }, (_, k) => <rect key={k} x={0} y={(k * C) / 7} width={L} height={C / 14} fill="#0e3323" />)}
      <g fill="none" stroke="rgba(255,255,255,.4)" strokeWidth={0.35}>
        <rect x={0} y={0} width={L} height={C} />
        <line x1={0} y1={C / 2} x2={L} y2={C / 2} />
        <circle cx={L / 2} cy={C / 2} r={9.15} />
        <rect x={L / 2 - 20.16} y={0} width={40.32} height={16.5} />
        <rect x={L / 2 - 9.16} y={0} width={18.32} height={5.5} />
        <rect x={L / 2 - 20.16} y={C - 16.5} width={40.32} height={16.5} />
        <rect x={L / 2 - 9.16} y={C - 5.5} width={18.32} height={5.5} />
      </g>
    </g>
  )
}

function Elenco({ elenco, emCampo, ativo, onEscolher }: {
  elenco: AtletaDaPrancheta[]; emCampo: Set<number | null>; ativo: boolean; onEscolher: (a: AtletaDaPrancheta) => void
}) {
  const grupos = Object.keys(SETORES).map((s) => [s, elenco.filter((a) => a.setor === s)] as const).filter(([, l]) => l.length > 0)
  return (
    <div className={`prancheta-elenco ${ativo ? 'ativo' : ''}`}>
      {grupos.map(([s, lista]) => (
        <div key={s}>
          <h4>{SETORES[s]}</h4>
          <div className="pe-lista">
            {lista.map((a) => (
              <button key={a.id} className={`pe-item ${emCampo.has(a.id) ? 'em-campo' : ''}`} onClick={() => onEscolher(a)} disabled={!ativo}
                title={`${a.funcao ?? ''}: ${a.titularidades} jogos como titular na temporada, ${a.recentes} nos últimos 10`}>
                {a.nome}
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}

/** Em linhas com 4 ou mais jogadores, um nome sim, um não vai para cima da bolinha: lado a lado eles se sobrepõem. */
function rotulosAcima(pecas: Peca[]) {
  const acima = new Set<number>()
  const linhas = new Map<number, number[]>()
  pecas.forEach((p, i) => { const k = Math.round(p.y / 6); linhas.set(k, [...(linhas.get(k) ?? []), i]) })
  for (const idx of linhas.values()) {
    if (idx.length < 4) continue
    idx.sort((a, b) => pecas[a].x - pecas[b].x).forEach((i, k) => { if (k % 2 === 1) acima.add(i) })
  }
  return acima
}

/** "Gabriel Veneno" cabe; nomes longos ficam com o último nome ("Ruan Tressoldi" → "Tressoldi"); inicial some ("K. Castaño" → "Castaño"). */
function curto(nome: string) {
  const n = nome.trim().replace(/^[A-ZÀ-Ú]\.\s+/, '') // "K. Castaño" → "Castaño"
  if (n.length <= 12) return n
  const partes = n.split(/\s+/)
  return partes.length > 1 ? partes.slice(-1)[0] : n.slice(0, 12)
}

/** O quadro em PNG: 1920x1080 (campo à esquerda, título à direita) ou 1080x1920 (título em cima, campo embaixo). */
async function baixarImagem(pecas: Peca[], titulo: string, esquema: string, formato: 'h' | 'v', cores: CoresDoClube,
  rivais: Peca[] = [], coresRival: CoresDoClube = cores, setas: Seta[] = []) {
  const [W, H] = formato === 'h' ? [1920, 1080] : [1080, 1920]
  const c = document.createElement('canvas')
  c.width = W; c.height = H
  const g = c.getContext('2d')!
  g.fillStyle = '#07170f'; g.fillRect(0, 0, W, H)
  const [fw, fh, fx, fy] = formato === 'h' ? [620, 960, 120, 60] : [880, 1360, 100, 380]
  const sx = fw / (L + 6), sy = fh / (C + 6)
  const X = (v: number) => fx + (v + 3) * sx, Y = (v: number) => fy + (v + 3) * sy
  g.fillStyle = '#0b2a1d'; g.fillRect(fx, fy, fw, fh)
  g.fillStyle = '#0e3323'
  for (let k = 0; k < 7; k++) g.fillRect(X(0), Y((k * C) / 7), L * sx, (C / 14) * sy)
  g.strokeStyle = 'rgba(255,255,255,.4)'; g.lineWidth = 3
  g.strokeRect(X(0), Y(0), L * sx, C * sy)
  g.beginPath(); g.moveTo(X(0), Y(C / 2)); g.lineTo(X(L), Y(C / 2)); g.stroke()
  g.beginPath(); g.ellipse(X(L / 2), Y(C / 2), 9.15 * sx, 9.15 * sy, 0, 0, Math.PI * 2); g.stroke()
  for (const [x, y, w, h] of [[L / 2 - 20.16, 0, 40.32, 16.5], [L / 2 - 9.16, 0, 18.32, 5.5], [L / 2 - 20.16, C - 16.5, 40.32, 16.5], [L / 2 - 9.16, C - 5.5, 18.32, 5.5]])
    g.strokeRect(X(x), Y(y), w * sx, h * sy)
  function time(lista: Peca[], c: CoresDoClube, lado: 'cima' | 'baixo' | null) {
    const acima = rotulosAcima(lista)
    for (const [i, p] of lista.entries()) {
      const emCima = lado ? lado === 'cima' : acima.has(i)
      const cx = X((p.x / 100) * L), cy = Y((p.y / 100) * C), ty = cy + (emCima ? -4.6 : 6.2) * sy
      g.beginPath(); g.arc(cx, cy, 3.3 * sx, 0, Math.PI * 2); g.fillStyle = c.camisa; g.fill()
      g.lineWidth = 6; g.strokeStyle = c.detalhe; g.stroke()
      g.strokeStyle = '#07170f'
      g.font = `800 ${Math.round(2.5 * sx)}px Inter, Arial, sans-serif`; g.textAlign = 'center'
      g.lineWidth = 8; g.strokeText(curto(p.nome) || '?', cx, ty); g.fillStyle = '#fff'; g.fillText(curto(p.nome) || '?', cx, ty)
    }
  }
  time(rivais, coresRival, 'cima')
  time(pecas, cores, rivais.length > 0 ? 'baixo' : null)
  for (const s of setas) {
    const [x1, y1, x2, y2] = [X((s.x1 / 100) * L), Y((s.y1 / 100) * C), X((s.x2 / 100) * L), Y((s.y2 / 100) * C)]
    const cor = s.tipo === 'passe' ? '#ffffff' : '#ffd23f'
    const ang = Math.atan2(y2 - y1, x2 - x1), ponta = 26
    g.strokeStyle = cor; g.fillStyle = cor; g.lineWidth = s.tipo === 'passe' ? 6 : 8; g.lineCap = 'round'
    g.setLineDash(s.tipo === 'passe' ? [16, 12] : [])
    g.beginPath(); g.moveTo(x1, y1); g.lineTo(x2 - Math.cos(ang) * ponta * 0.6, y2 - Math.sin(ang) * ponta * 0.6); g.stroke()
    g.setLineDash([])
    g.beginPath(); g.moveTo(x2, y2)
    g.lineTo(x2 - ponta * Math.cos(ang - 0.45), y2 - ponta * Math.sin(ang - 0.45))
    g.lineTo(x2 - ponta * Math.cos(ang + 0.45), y2 - ponta * Math.sin(ang + 0.45)); g.closePath(); g.fill()
  }
  g.textAlign = 'left'
  const [tx, ty, tw] = formato === 'h' ? [880, 200, 920] : [100, 140, 880]
  g.fillStyle = '#0f9d58'; g.font = '800 40px Inter, Arial, sans-serif'
  const sw = g.measureText(esquema).width + 56
  g.beginPath(); g.roundRect(tx, ty - 50, sw, 66, 33); g.fill()
  g.fillStyle = '#fff'; g.fillText(esquema, tx + 28, ty - 4)
  g.font = `900 ${formato === 'h' ? 84 : 72}px Inter, Arial, sans-serif`
  let linha = '', yy = ty + 110
  for (const p of titulo.split(' ')) {
    if (g.measureText(`${linha} ${p}`.trim()).width > tw) { g.fillText(linha, tx, yy); linha = p; yy += formato === 'h' ? 96 : 84 } else linha = `${linha} ${p}`.trim()
  }
  g.fillText(linha, tx, yy)
  g.font = '800 34px Inter, Arial, sans-serif'; g.fillStyle = 'rgba(255,255,255,.8)'
  g.fillText('Futebol em', formato === 'h' ? 880 : 100, H - 70)
  g.fillStyle = '#4fe3a0'; g.fillText('Números', (formato === 'h' ? 880 : 100) + g.measureText('Futebol em ').width, H - 70)
  await baixarCartao({ nome: `prancheta-${esquema}`, canvas: c })
}
