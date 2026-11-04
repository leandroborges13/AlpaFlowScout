import type { AnalisePartida } from './api'

// "Para vídeo": cartões 1080x1920 (9:16) para os Shorts do canal Futebol em Números, desenhados em canvas e baixados como PNG.
// O conteúdo fica na janela 1000x735 em (40, 330), a mesma da moldura dos Shorts: o cartão entra direto na montagem.
// Embaixo da janela fica espaço para o rosto e a legenda; no rodapé, a marca do canal.
const L = 1080, A = 1920
const JANELA = { x: 40, y: 330, w: 1000, h: 735 }
const COR = { fundo: '#07170f', painel: '#0d2a1d', linha: '#1f4a35', verde: '#4fe3a0', verdeEscuro: '#0f9d58', texto: '#ffffff', suave: '#b9e8cf', rival: '#7c8b84' }
const FONTE = 'Inter, system-ui, sans-serif'

type Ctx = CanvasRenderingContext2D

function novaTela(): [HTMLCanvasElement, Ctx] {
  const c = document.createElement('canvas')
  c.width = L
  c.height = A
  const ctx = c.getContext('2d')!
  const g = ctx.createRadialGradient(L / 2, 700, 100, L / 2, 700, 1300)
  g.addColorStop(0, '#0d3423')
  g.addColorStop(1, COR.fundo)
  ctx.fillStyle = g
  ctx.fillRect(0, 0, L, A)
  return [c, ctx]
}

function fonte(ctx: Ctx, tamanho: number, peso = 700) {
  ctx.font = `${peso} ${tamanho}px ${FONTE}`
}

/** Quebra o texto em linhas que cabem na largura (respeita as quebras de linha do texto). */
function linhas(ctx: Ctx, texto: string, largura: number): string[] {
  const saida: string[] = []
  for (const paragrafo of texto.split('\n')) {
    let atual = ''
    for (const palavra of paragrafo.split(/\s+/).filter(Boolean)) {
      const teste = atual ? `${atual} ${palavra}` : palavra
      if (ctx.measureText(teste).width > largura && atual) { saida.push(atual); atual = palavra }
      else atual = teste
    }
    saida.push(atual)
  }
  return saida
}

/** Escreve o texto no maior tamanho (entre max e min) que cabe na caixa; no mínimo, corta com reticências. */
function textoNaCaixa(ctx: Ctx, texto: string, x: number, y: number, w: number, h: number, max: number, min: number, cor = COR.texto, peso = 600) {
  let tam = max
  let ls: string[] = []
  for (; tam >= min; tam -= 2) {
    fonte(ctx, tam, peso)
    ls = linhas(ctx, texto, w)
    if (ls.length * tam * 1.3 <= h) break
  }
  tam = Math.max(tam, min)
  const cabem = Math.floor(h / (tam * 1.3))
  if (ls.length > cabem) ls = [...ls.slice(0, cabem - 1), `${ls[cabem - 1]}…`]
  ctx.fillStyle = cor
  ctx.textBaseline = 'top'
  ls.forEach((l, i) => ctx.fillText(l, x, y + i * tam * 1.3))
  return y + ls.length * tam * 1.3
}

function caixa(ctx: Ctx, x: number, y: number, w: number, h: number, r: number, cor: string) {
  ctx.beginPath()
  ctx.roundRect(x, y, w, h, r)
  ctx.fillStyle = cor
  ctx.fill()
}

/** Selo e título no topo (acima da janela), a janela vazia e a marca no rodapé. */
function moldura(ctx: Ctx, selo: string, titulo: string, tamanhoTitulo = 64) {
  fonte(ctx, 30, 800)
  const ws = ctx.measureText(selo).width + 44
  caixa(ctx, (L - ws) / 2, 90, ws, 52, 26, COR.verdeEscuro)
  ctx.fillStyle = COR.texto
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(selo, L / 2, 117)
  fonte(ctx, tamanhoTitulo, 800)
  // o título tem duas linhas; o que passar disso termina em reticências
  let ls = linhas(ctx, titulo, 960)
  if (ls.length > 2) ls = [ls[0], `${ls[1].replace(/[s,.;:]+$/, '')}…`]
  ctx.textBaseline = 'top'
  ls.forEach((l, i) => ctx.fillText(l, L / 2, 168 + i * tamanhoTitulo * 1.12))
  ctx.textAlign = 'left'
  caixa(ctx, JANELA.x, JANELA.y, JANELA.w, JANELA.h, 32, COR.painel)
  ctx.textAlign = 'center'
  fonte(ctx, 34, 800)
  ctx.fillStyle = COR.texto
  ctx.fillText('Futebol em Números', L / 2, A - 150)
  fonte(ctx, 26, 600)
  ctx.fillStyle = COR.verde
  ctx.fillText('análise com a IA do AlpaFlow Scout · scout.alpaflow.tec.br', L / 2, A - 102)
  ctx.textAlign = 'left'
}

async function baixar(c: HTMLCanvasElement, nome: string) {
  const blob = await new Promise<Blob | null>((ok) => c.toBlob(ok, 'image/png'))
  if (!blob) return
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `${nome}.png`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 5000)
}

const nomeDeArquivo = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-zA-Z0-9]+/g, '-').replace(/^-|-$/g, '').toLowerCase().slice(0, 50)

async function prontoParaDesenhar() {
  try { await Promise.all([document.fonts.load(`800 64px Inter`), document.fonts.load(`600 40px Inter`)]) } catch { /* sem a fonte: usa a do sistema */ }
}

/** Cartão de uma resposta do chat: a pergunta no topo e a resposta na janela, no maior tamanho que couber. */
export async function cartaoDaResposta(pergunta: string, resposta: string) {
  await prontoParaDesenhar()
  const [c, ctx] = novaTela()
  moldura(ctx, 'Perguntei para a IA', pergunta, 50)
  textoNaCaixa(ctx, resposta, JANELA.x + 50, JANELA.y + 50, JANELA.w - 100, JANELA.h - 100, 46, 26)
  await baixar(c, `para-video-${nomeDeArquivo(pergunta)}`)
}

/** Três cartões do jogo, na ordem do roteiro: placar e gols, onde o jogo foi ganho (aspectos) e os destaques pela nota. */
export async function cartoesDaPartida(a: AnalisePartida) {
  await prontoParaDesenhar()
  const p = a.partida
  const jogo = `${p.mandante} ${p.golsMandante ?? ''} x ${p.golsVisitante ?? ''} ${p.visitante}`
  const selo = [p.competicao, p.rodada?.replace('Regular Season - ', 'Rodada ')].filter(Boolean).join(' · ')
  const nome = nomeDeArquivo(`${p.mandante}-${p.visitante}`)
  const time = (clubeId: number) => (clubeId === p.mandanteId ? p.mandante : p.visitante)

  // 1. Placar e gols
  {
    const [c, ctx] = novaTela()
    moldura(ctx, selo, 'O jogo')
    ctx.textAlign = 'center'
    fonte(ctx, 44, 800)
    ctx.fillStyle = COR.suave
    ctx.fillText(p.mandante, L / 2 - 250, JANELA.y + 70)
    ctx.fillText(p.visitante, L / 2 + 250, JANELA.y + 70)
    fonte(ctx, 150, 800)
    ctx.fillStyle = COR.texto
    ctx.fillText(`${p.golsMandante ?? '-'}  x  ${p.golsVisitante ?? '-'}`, L / 2, JANELA.y + 130)
    if (a.desfecho) { fonte(ctx, 32, 700); ctx.fillStyle = COR.verde; ctx.fillText(a.desfecho, L / 2, JANELA.y + 300) }
    ctx.textAlign = 'left'
    const gols = [...a.eventos].filter((e) => e.tipo.startsWith('Gol')).sort((x, y) => x.minuto - y.minuto || (x.acrescimo ?? 0) - (y.acrescimo ?? 0))
    let y = JANELA.y + 370
    for (const g of gols.slice(0, 7)) {
      const minuto = g.acrescimo ? `${g.minuto}+${g.acrescimo}'` : `${g.minuto}'`
      fonte(ctx, 34, 800)
      ctx.fillStyle = COR.verde
      ctx.fillText(minuto, JANELA.x + 60, y)
      fonte(ctx, 34, 600)
      ctx.fillStyle = COR.texto
      const extra = g.tipo === 'Gol' ? '' : ` (${g.tipo.replace('Gol ', '').replace('de ', '')})`
      ctx.fillText(`${g.jogador ?? '?'}${extra} · ${time(g.clubeId)}`, JANELA.x + 210, y)
      y += 50
    }
    await baixar(c, `${nome}-1-placar`)
  }

  // 2. Onde o jogo foi ganho: percentil de cada time contra os próprios jogos da temporada
  {
    const [c, ctx] = novaTela()
    moldura(ctx, jogo, 'Onde o jogo foi decidido')
    // até 5 aspectos, os de vantagem clara primeiro (o "equilibrado" diz pouco no vídeo)
    const aspectos = a.aspectos.filter((x) => x.percentilMandante != null && x.percentilVisitante != null)
      .sort((x, y) => Number(x.vantagem === 'Equilibrado') - Number(y.vantagem === 'Equilibrado')).slice(0, 5)
    fonte(ctx, 24, 700)
    ctx.fillStyle = COR.suave
    ctx.fillText(`■ ${p.mandante}`, JANELA.x + 50, JANELA.y + 36)
    ctx.fillStyle = COR.rival
    ctx.fillText(`■ ${p.visitante}`, JANELA.x + 420, JANELA.y + 36)
    let y = JANELA.y + 90
    const largura = JANELA.w - 100
    for (const x of aspectos) {
      fonte(ctx, 30, 700)
      ctx.fillStyle = COR.texto
      ctx.fillText(x.aspecto, JANELA.x + 50, y)
      for (const [k, valor, cor] of [[0, x.percentilMandante!, COR.verde], [1, x.percentilVisitante!, COR.rival]] as const) {
        const by = y + 44 + k * 26
        caixa(ctx, JANELA.x + 50, by, largura - 90, 18, 9, COR.linha)
        caixa(ctx, JANELA.x + 50, by, Math.max(18, ((largura - 90) * valor) / 100), 18, 9, cor)
        fonte(ctx, 22, 800)
        ctx.fillStyle = cor
        ctx.fillText(`${Math.round(valor)}`, JANELA.x + 50 + largura - 70, by - 3)
      }
      y += 120
    }
    fonte(ctx, 22, 600)
    ctx.fillStyle = COR.suave
    ctx.fillText('Percentil: o jogo comparado com os próprios jogos do time na temporada (0 a 100)', JANELA.x + 50, JANELA.y + JANELA.h - 50)
    await baixar(c, `${nome}-2-onde-foi-decidido`)
  }

  // 3. Destaques pela nota da IA
  {
    const [c, ctx] = novaTela()
    moldura(ctx, jogo, 'Os destaques pela IA')
    const destaques = (a.desempenhos ?? []).filter((d) => d.nota != null).sort((x, y) => y.nota! - x.nota!).slice(0, 5)
    let y = JANELA.y + 50
    for (const d of destaques) {
      caixa(ctx, JANELA.x + 40, y, JANELA.w - 80, 106, 22, '#123726')
      fonte(ctx, 64, 800)
      ctx.fillStyle = COR.verde
      ctx.textAlign = 'center'
      ctx.fillText(`${Math.round(d.nota!)}`, JANELA.x + 130, y + 20)
      ctx.textAlign = 'left'
      fonte(ctx, 38, 800)
      ctx.fillStyle = COR.texto
      ctx.fillText(d.nome, JANELA.x + 230, y + 14)
      fonte(ctx, 26, 600)
      ctx.fillStyle = COR.suave
      const feitos = [d.gols ? `${d.gols} gol${d.gols > 1 ? 's' : ''}` : '', d.assistencias ? `${d.assistencias} assist.` : ''].filter(Boolean).join(' · ')
      ctx.fillText([time(d.clubeId), d.funcao, feitos].filter(Boolean).join(' · '), JANELA.x + 230, y + 64)
      y += 120
    }
    fonte(ctx, 22, 600)
    ctx.fillStyle = COR.suave
    ctx.fillText('Nota do jogo de 0 a 100, calculada pela IA do AlpaFlow Scout', JANELA.x + 50, JANELA.y + JANELA.h - 50)
    await baixar(c, `${nome}-3-destaques`)
  }
}
