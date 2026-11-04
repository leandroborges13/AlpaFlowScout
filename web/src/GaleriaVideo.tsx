import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { baixarCartao, type Cartao } from './ParaVideo'

/**
 * Os cartões do "Para vídeo" na tela, em vez de baixar um por um: a galeria mostra todos lado a lado (a análise junta) e, ao
 * clicar num, ele abre grande, do tamanho da tela, para gravar direto com o OBS. No modo grande: ← e → passam, H esconde os
 * botões, F põe em tela cheia, Esc volta. "Baixar" continua para quem quiser os arquivos.
 */
/** Desenhada no body (portal): dentro do painel da partida, o modal e a tela cheia ficariam presos a ele. */
export function GaleriaVideo(props: { cartoes: Cartao[]; titulo: string; onFechar: () => void }) {
  return createPortal(<Galeria {...props} />, document.body)
}

function Galeria({ cartoes, titulo, onFechar }: { cartoes: Cartao[]; titulo: string; onFechar: () => void }) {
  const urls = useMemo(() => cartoes.map((c) => c.canvas.toDataURL('image/png')), [cartoes])
  const [aberto, setAberto] = useState<number | null>(cartoes.length === 1 ? 0 : null)
  const [semBotoes, setSemBotoes] = useState(false)
  const palco = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const tecla = (e: KeyboardEvent) => {
      // a galeria abre por cima do painel da partida, que também fecha com Esc: as teclas daqui não chegam até ele
      if (['Escape', 'ArrowLeft', 'ArrowRight', 'h', 'H', 'f', 'F'].includes(e.key)) e.stopImmediatePropagation()
      if (aberto == null) { if (e.key === 'Escape') onFechar(); return }
      // Esc sai da tela cheia primeiro (o navegador cuida); depois volta à galeria, ou fecha quando é um cartão só
      if (e.key === 'Escape') { if (document.fullscreenElement) return; if (cartoes.length === 1) onFechar(); else setAberto(null) }
      else if (e.key === 'ArrowRight') setAberto((i) => Math.min(cartoes.length - 1, (i ?? 0) + 1))
      else if (e.key === 'ArrowLeft') setAberto((i) => Math.max(0, (i ?? 0) - 1))
      else if (e.key.toLowerCase() === 'h') setSemBotoes((v) => !v)
      else if (e.key.toLowerCase() === 'f') telaCheia()
    }
    window.addEventListener('keydown', tecla, true)
    return () => window.removeEventListener('keydown', tecla, true)
  })

  function telaCheia() {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
    else palco.current?.requestFullscreen().catch(() => {})
  }

  async function baixarTodas() {
    for (const c of cartoes) await baixarCartao(c)
  }

  if (aberto != null) {
    const c = cartoes[aberto]
    return (
      <div className="galeria-palco" ref={palco} onClick={(e) => { if (e.target === e.currentTarget && !semBotoes) setAberto(null) }}>
        <img src={urls[aberto]} alt={c.nome} />
        {!semBotoes && (
          <div className="galeria-controles">
            <button className="btn" onClick={() => setAberto(Math.max(0, aberto - 1))} disabled={aberto === 0}>←</button>
            <span>{aberto + 1} de {cartoes.length}</span>
            <button className="btn" onClick={() => setAberto(Math.min(cartoes.length - 1, aberto + 1))} disabled={aberto === cartoes.length - 1}>→</button>
            <button className="btn" onClick={telaCheia} title="Tela cheia (F)">⛶ Tela cheia</button>
            <button className="btn" onClick={() => setSemBotoes(true)} title="Esconde os botões para gravar (H mostra de novo)">Esconder botões</button>
            <button className="btn" onClick={() => baixarCartao(c)}>Baixar esta</button>
            <button className="btn" onClick={() => (cartoes.length === 1 ? onFechar() : setAberto(null))}>{cartoes.length === 1 ? 'Fechar' : 'Voltar'}</button>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel largo galeria" role="dialog" aria-modal aria-label={titulo} onClick={(e) => e.stopPropagation()}>
        <div className="galeria-topo">
          <h3>🎬 {titulo}</h3>
          <div className="pos-jogo-acoes">
            <button className="btn" onClick={baixarTodas}>Baixar todas</button>
            <button className="btn" onClick={onFechar}>Fechar</button>
          </div>
        </div>
        <p className="legenda">Clique num cartão para ver grande e gravar. No modo grande: ← → passam, H esconde os botões, F põe em tela cheia.</p>
        <div className="galeria-lista">
          {cartoes.map((c, i) => (
            <button key={c.nome} className="galeria-item" onClick={() => setAberto(i)} title={c.nome}>
              <img src={urls[i]} alt={c.nome} />
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
