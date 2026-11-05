import type { AnalisePartida, CalorDoJogador, DesempenhoNoJogo, FinalizacaoNoMapa, MapaDaPartida } from './api'

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

/** Um cartão pronto: o nome do arquivo (sem .png) e a imagem. */
export type Cartao = { nome: string; canvas: HTMLCanvasElement }

// Durante coletar(), os cartões vão para a galeria da tela em vez de baixar
let coletados: Cartao[] | null = null

async function baixar(c: HTMLCanvasElement, nome: string) {
  if (coletados) { coletados.push({ nome, canvas: c }); return }
  await baixarCartao({ nome, canvas: c })
}

export async function baixarCartao({ nome, canvas }: Cartao) {
  const blob = await new Promise<Blob | null>((ok) => canvas.toBlob(ok, 'image/png'))
  if (!blob) return
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `${nome}.png`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 5000)
}

/** Roda os geradores (cartaoDaResposta, cartoesDaPartida, cartaoDoJogador) e devolve os cartões em vez de baixar. */
export async function coletar(gerar: () => Promise<unknown>): Promise<Cartao[]> {
  coletados = []
  try { await gerar(); return coletados } finally { coletados = null }
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

/**
 * Cartões do jogo, na ordem do roteiro: placar e gols, onde o jogo foi ganho (aspectos), os destaques pela nota e, quando há o
 * mapa (ligas, pós-jogo), onde cada time finalizou e onde o time do canal teve a bola.
 */
export async function cartoesDaPartida(a: AnalisePartida, mapa?: MapaDaPartida | null) {
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

  await cartaoDasNotas(a, jogo, nome)

  if (mapa) {
    await cartaoDoMapa(a, mapa, jogo, nome)
    // 5º: o mapa do destaque do time do canal (a maior nota entre os que a fonte do mapa também tem)
    const foco = p.visitanteId === TIME_DO_CANAL ? p.visitanteId : p.mandanteId
    const destaque = (a.desempenhos ?? []).filter((d) => d.clubeId === foco && d.nota != null)
      .sort((x, y) => y.nota! - x.nota!).find((d) => ligarJogador(d.nome, d.clubeId, mapa))
    if (destaque) await cartaoDoJogador(a, mapa, destaque, '5-destaque')
  }
}

/** Cor da nota (0 a 100): azul é jogo muito bom, verde bom, amarelo regular, laranja abaixo. */
export function corDaNota(nota: number | null | undefined) {
  if (nota == null) return '#5b6b63'
  return nota >= 70 ? '#2f9bff' : nota >= 55 ? '#1fbf6a' : nota >= 40 ? '#e0b10f' : '#f07a2a'
}

/**
 * As notas do time do canal (ou do mandante) no desenho tático: o campo deitado com os titulares, cada um com a nota numa
 * bolinha colorida e o nome embaixo, uma estrela no melhor do time e, no rodapé da janela, quem entrou com a nota.
 */
async function cartaoDasNotas(a: AnalisePartida, jogo: string, nome: string) {
  const p = a.partida
  const foco = p.visitanteId === TIME_DO_CANAL ? 'visitante' : 'mandante'
  const lado = a[foco]
  const clubeId = foco === 'visitante' ? p.visitanteId : p.mandanteId
  if (!lado.titulares.some((j) => j.x != null)) return
  const notas = new Map((a.desempenhos ?? []).map((d) => [d.atletaId, d]))
  const titulares = new Set(lado.titulares.map((j) => j.id))
  const entraram = (a.desempenhos ?? []).filter((d) => d.clubeId === clubeId && !titulares.has(d.atletaId))
  const doTime = (a.desempenhos ?? []).filter((d) => d.clubeId === clubeId && d.nota != null)
  const melhor = doTime.length ? doTime.reduce((m, d) => (d.nota! > m.nota! ? d : m)).atletaId : null

  const [c, ctx] = novaTela()
  moldura(ctx, jogo, `As notas do ${lado.time}${lado.esquema ? ` · ${lado.esquema}` : ''}`, 56)
  // campo deitado: o time ataca para a direita, a esquerda dele fica em cima
  const cx = JANELA.x + 30, cy = JANELA.y + 24, cw = JANELA.w - 60, ch = JANELA.h - 150
  caixa(ctx, cx, cy, cw, ch, 18, '#0f3a27')
  ctx.strokeStyle = 'rgba(255,255,255,.28)'
  ctx.lineWidth = 3
  ctx.strokeRect(cx + 10, cy + 10, cw - 20, ch - 20)
  ctx.beginPath(); ctx.moveTo(cx + cw / 2, cy + 10); ctx.lineTo(cx + cw / 2, cy + ch - 10); ctx.stroke()
  ctx.beginPath(); ctx.arc(cx + cw / 2, cy + ch / 2, ch * 0.14, 0, Math.PI * 2); ctx.stroke()
  for (const lx of [cx + 10, cx + cw - 10 - cw * 0.157]) ctx.strokeRect(lx, cy + ch * 0.2, cw * 0.157, ch * 0.6)

  const curto = (n: string) => { const s = n.includes('. ') ? n.slice(n.lastIndexOf('. ') + 2) : n; return s.length > 11 ? s.split(/\s+/).pop()! : s }
  for (const j of lado.titulares) {
    if (j.x == null || j.y == null) continue
    const px = cx + 40 + ((j.y <= 5 ? 2 : j.y) / 92) * (cw - 90)
    const py = cy + 22 + (j.x / 100) * (ch - 78)
    const d = j.id != null ? notas.get(j.id) : undefined
    ctx.beginPath(); ctx.arc(px, py, 28, 0, Math.PI * 2); ctx.fillStyle = corDaNota(d?.nota); ctx.fill()
    ctx.lineWidth = 4; ctx.strokeStyle = COR.fundo; ctx.stroke()
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle'
    fonte(ctx, 26, 900); ctx.fillStyle = '#fff'
    ctx.fillText(d?.nota != null ? `${Math.round(d.nota)}` : '–', px, py + 1)
    if (d && d.atletaId === melhor) { fonte(ctx, 22, 900); ctx.fillText('⭐', px + 26, py - 22) }
    fonte(ctx, 22, 800)
    ctx.lineWidth = 6; ctx.strokeStyle = COR.fundo; ctx.strokeText(curto(j.nome), px, py + 44)
    ctx.fillStyle = '#fff'; ctx.fillText(curto(j.nome), px, py + 44)
  }
  // quem entrou
  ctx.textAlign = 'left'; ctx.textBaseline = 'middle'
  let x = JANELA.x + 40
  const y = cy + ch + 46
  fonte(ctx, 26, 800); ctx.fillStyle = COR.suave
  ctx.fillText('Entraram:', x, y); x += ctx.measureText('Entraram: ').width + 6
  for (const d of entraram.slice(0, 5)) {
    const t = curto(d.nome)
    fonte(ctx, 24, 800)
    const w = ctx.measureText(t).width + 76
    if (x + w > JANELA.x + JANELA.w - 30) break
    caixa(ctx, x, y - 24, w, 48, 24, '#123726')
    ctx.beginPath(); ctx.arc(x + 24, y, 17, 0, Math.PI * 2); ctx.fillStyle = corDaNota(d.nota); ctx.fill()
    fonte(ctx, 17, 900); ctx.fillStyle = '#fff'; ctx.textAlign = 'center'
    ctx.fillText(d.nota != null ? `${Math.round(d.nota)}` : '–', x + 24, y + 1)
    ctx.textAlign = 'left'; fonte(ctx, 24, 800); ctx.fillText(t, x + 50, y + 1)
    x += w + 10
  }
  fonte(ctx, 22, 600); ctx.fillStyle = COR.suave; ctx.textBaseline = 'top'
  ctx.fillText('Nota de 0 a 100 pela IA do Scout · ⭐ melhor do time · – menos de 30 min em campo', JANELA.x + 40, JANELA.y + JANELA.h - 40)
  await baixar(c, `${nome}-3b-notas`)
}

const SEM_PESO = new Set(['de', 'da', 'do', 'dos', 'das', 'jr', 'junior', 'filho', 'neto'])
const tokens = (nome: string) => nome.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  .split(/[^a-z]+/).filter((t) => t.length >= 3 && !SEM_PESO.has(t))
function distancia(a: string, b: string) {
  const d = Array.from({ length: b.length + 1 }, (_, j) => j)
  for (let i = 1; i <= a.length; i++) {
    let anterior = d[0]
    d[0] = i
    for (let j = 1; j <= b.length; j++) {
      const guardado = d[j]
      d[j] = Math.min(d[j] + 1, d[j - 1] + 1, anterior + (a[i - 1] === b[j - 1] ? 0 : 1))
      anterior = guardado
    }
  }
  return d[b.length]
}
const parecidos = (a: string, b: string) => a === b || (Math.min(a.length, b.length) >= 4 && (distancia(a, b) <= 1 || a.startsWith(b) || b.startsWith(a)))

/**
 * O mapa de calor do nosso atleta na fonte do mapa, que escreve os nomes de outro jeito ("Vitor Hugo" x "Victor Hugo",
 * "K. Castaño" x "Kevin Castaño"). Só no mesmo time, e só quando todas as partes do nome mais curto batem com o outro.
 * Empate ou nada parecido: null (melhor não mostrar do que mostrar o mapa de outra pessoa).
 */
export function ligarJogador(nome: string, clubeId: number, mapa: MapaDaPartida): CalorDoJogador | null {
  const nossos = tokens(nome)
  if (nossos.length === 0) return null
  const candidatos = (mapa.jogadores ?? []).filter((j) => j.clubeId === clubeId).map((j) => {
    const deles = tokens(j.nome)
    const [curto, longo] = nossos.length <= deles.length ? [nossos, deles] : [deles, nossos]
    const batem = curto.filter((t) => longo.some((o) => parecidos(t, o))).length
    // parte idêntica vale mais que parecida: o Galo tem "Vitor Hugo" (zagueiro) e "Victor Hugo" (meia), dois jogadores
    const iguais = curto.filter((t) => longo.includes(t)).length
    return { j, ok: curto.length > 0 && batem === curto.length, pontos: batem + iguais }
  }).filter((c) => c.ok).sort((x, y) => y.pontos - x.pontos)
  if (candidatos.length === 0 || (candidatos.length > 1 && candidatos[1].pontos === candidatos[0].pontos)) return null
  return candidatos[0].j
}

/**
 * Cartão de um jogador: na janela, onde ele teve a bola (mancha de calor, o time dele atacando para a direita) e as finalizações
 * dele por cima. Devolve false quando a fonte do mapa não tem o jogador (aí não baixa nada).
 */
export async function cartaoDoJogador(a: AnalisePartida, mapa: MapaDaPartida, d: DesempenhoNoJogo, sufixo = 'jogador') {
  const calor = ligarJogador(d.nome, d.clubeId, mapa)
  if (!calor || calor.celulas.length === 0) return false
  await prontoParaDesenhar()
  const p = a.partida
  const [c, ctx] = novaTela()
  moldura(ctx, `${p.mandante} ${p.golsMandante ?? ''} x ${p.golsVisitante ?? ''} ${p.visitante}`, `Onde ${d.nome} jogou`)
  const feitos = [d.gols ? `${d.gols} gol${d.gols > 1 ? 's' : ''}` : '', d.assistencias ? `${d.assistencias} assist.` : ''].filter(Boolean)
  fonte(ctx, 30, 800)
  ctx.textBaseline = 'middle'
  ctx.fillStyle = COR.verde
  ctx.fillText([d.nota != null ? `Nota ${Math.round(d.nota)} da IA` : '', d.funcao, ...feitos].filter(Boolean).join(' · '), JANELA.x + 40, JANELA.y + 45)
  const k = mancha(ctx, JANELA.x + 40, JANELA.y + 85, JANELA.w - 80, calor.celulas, d.clubeId !== p.mandanteId)
  // as finalizações dele (a fonte sempre manda atacando para a direita, como o campo acima)
  const chutes = mapa.finalizacoes.filter((f) => f.clubeId === d.clubeId && f.jogador === calor.nome)
  for (const f of chutes) {
    ctx.beginPath()
    ctx.arc(k.px(f.x), k.py(f.y), 9 + Math.sqrt(f.xg) * 42, 0, Math.PI * 2)
    ctx.fillStyle = 'rgba(79,227,160,.75)'
    ctx.fill()
    ctx.lineWidth = f.gol ? 5 : 2
    ctx.strokeStyle = f.gol ? '#ffd166' : '#ffffff'
    ctx.stroke()
  }
  fonte(ctx, 24, 700)
  ctx.textBaseline = 'top'
  ctx.fillStyle = COR.texto
  const finalizacoes = chutes.length ? ` · ${chutes.length} finalizaç${chutes.length > 1 ? 'ões' : 'ão'} (bolinhas)` : ''
  ctx.fillText(`${calor.acoes} ações com a bola${finalizacoes} · ataca para a direita →`, JANELA.x + 40, JANELA.y + 85 + k.h + 18)
  fonte(ctx, 20, 600)
  ctx.fillStyle = COR.suave
  ctx.fillText(`Quanto mais quente, mais vezes ele teve a bola ali · dados: ${mapa.fonte}`, JANELA.x + 40, JANELA.y + 85 + k.h + 56)
  await baixar(c, `${nomeDeArquivo(`${p.mandante}-${p.visitante}`)}-${sufixo}-${nomeDeArquivo(d.nome)}`)
  return true
}

/**
 * "No campo" do painel da partida: o mapa de finalizações (a janela do 4º cartão) e o de calor (a parte de baixo), recortados
 * do cartão para a análise na tela. calor é nulo quando a fonte não tem o mapa do time.
 */
export async function recortesDoMapa(a: AnalisePartida, mapa: MapaDaPartida): Promise<{ finalizacoes: string; calor: string | null }> {
  await prontoParaDesenhar()
  const p = a.partida
  const [cartao] = await coletar(() => cartaoDoMapa(a, mapa, `${p.mandante} x ${p.visitante}`, 'mapa'))
  const recorte = (y: number, h: number) => {
    const c = document.createElement('canvas')
    c.width = JANELA.w
    c.height = h
    c.getContext('2d')!.drawImage(cartao.canvas, JANELA.x, y, JANELA.w, h, 0, 0, JANELA.w, h)
    return c.toDataURL('image/png')
  }
  const foco = p.visitanteId === TIME_DO_CANAL ? p.visitanteId : p.mandanteId
  return {
    finalizacoes: recorte(JANELA.y, JANELA.h),
    calor: mapa.calor.some((t) => t.clubeId === foco && t.celulas.length > 0) ? recorte(JANELA.y + JANELA.h + 5, 700) : null,
  }
}

/** O Galo (id 1062 no provedor) é o time do canal: quando joga, é ele que aparece atacando para a direita e no mapa de calor. */
const TIME_DO_CANAL = 1062

/** Campo deitado (105 x 68 m) com as linhas principais; devolve a altura e a conversão de metros para a tela. */
function campo(ctx: Ctx, x: number, y: number, w: number, fundo = true) {
  const h = (w * 68) / 105
  if (fundo) caixa(ctx, x, y, w, h, 8, '#0f3b27')
  ctx.strokeStyle = 'rgba(255,255,255,.45)'
  ctx.lineWidth = 3
  ctx.strokeRect(x, y, w, h)
  ctx.beginPath(); ctx.moveTo(x + w / 2, y); ctx.lineTo(x + w / 2, y + h); ctx.stroke()
  ctx.beginPath(); ctx.arc(x + w / 2, y + h / 2, (9.15 / 105) * w, 0, Math.PI * 2); ctx.stroke()
  const ax = (16.5 / 105) * w, ay = (40.3 / 68) * h
  ctx.strokeRect(x, y + (h - ay) / 2, ax, ay)
  ctx.strokeRect(x + w - ax, y + (h - ay) / 2, ax, ay)
  return { h, px: (m: number) => x + (m / 105) * w, py: (m: number) => y + (m / 68) * h }
}

/**
 * Campo com a mancha de calor da grade 16x12 ([x, y, ações], mandante atacando para a direita); `espelha` vira o campo para o
 * visitante também atacar para a direita. A grade numa telinha, ampliada com suavização e desfoque, vira a "mancha".
 */
function mancha(ctx: Ctx, x0: number, y0: number, largura: number, celulas: number[][], espelha: boolean) {
  const k = campo(ctx, x0, y0, largura)
  const mini = document.createElement('canvas')
  mini.width = 16
  mini.height = 12
  const m = mini.getContext('2d')!
  // pico pelo percentil 90 (uma célula fora da curva não apaga o resto) e as de pouca ação quase transparentes:
  // assim aparece onde se concentrou, e não uma mancha uniforme
  const valores = celulas.map((cel) => cel[2]).sort((x, y) => x - y)
  const pico = valores[Math.floor(valores.length * 0.9)] || 1
  for (const [cx, cy, n] of celulas) {
    const t = Math.min(1, n / pico)
    m.fillStyle = `rgba(255, ${Math.round(225 - 175 * t)}, 60, ${Math.pow(t, 1.6).toFixed(2)})`
    m.fillRect(espelha ? 15 - cx : cx, espelha ? 11 - cy : cy, 1, 1)
  }
  ctx.save()
  ctx.beginPath()
  ctx.rect(x0, y0, largura, k.h)
  ctx.clip()
  ctx.imageSmoothingEnabled = true
  ctx.filter = 'blur(16px)'
  ctx.globalAlpha = 0.95
  ctx.drawImage(mini, x0, y0, largura, k.h)
  ctx.restore()
  campo(ctx, x0, y0, largura, false) // as linhas por cima da mancha
  return k
}

/**
 * 4º cartão: na janela, onde cada time finalizou (bolinha do tamanho da chance de gol, gol com contorno amarelo); embaixo, onde o
 * time do canal teve a bola (mapa de calor suavizado). A fonte entrega toda finalização atacando para a direita: a do rival é
 * espelhada para atacar para a esquerda. No mapa de calor o mandante ataca para a direita.
 */
async function cartaoDoMapa(a: AnalisePartida, mapa: MapaDaPartida, jogo: string, nome: string) {
  const p = a.partida
  const foco = p.visitanteId === TIME_DO_CANAL ? p.visitanteId : p.mandanteId
  const rivalId = foco === p.mandanteId ? p.visitanteId : p.mandanteId
  const nomeDe = (id: number) => (id === p.mandanteId ? p.mandante : p.visitante)
  const [c, ctx] = novaTela()
  moldura(ctx, jogo, 'Onde cada time finalizou')

  fonte(ctx, 26, 800)
  ctx.textBaseline = 'middle'
  ctx.fillStyle = COR.verde
  ctx.fillText(`● ${nomeDe(foco)} ataca →`, JANELA.x + 40, JANELA.y + 45)
  ctx.fillStyle = COR.texto
  ctx.textAlign = 'right'
  ctx.fillText(`← ${nomeDe(rivalId)} ataca ●`, JANELA.x + JANELA.w - 40, JANELA.y + 45)
  ctx.textAlign = 'left'
  // campo um pouco menor que a janela: o resumo e a legenda cabem dentro dela (o recorte do "No campo" e o Short usam só a janela)
  const g = campo(ctx, JANELA.x + 60, JANELA.y + 75, JANELA.w - 120)
  // chances maiores primeiro, para a bolinha grande ficar por baixo e não esconder as outras
  for (const f of [...mapa.finalizacoes].sort((x, y) => y.xg - x.xg)) {
    const ehFoco = f.clubeId === foco
    ctx.beginPath()
    ctx.arc(g.px(ehFoco ? f.x : 105 - f.x), g.py(ehFoco ? f.y : 68 - f.y), 9 + Math.sqrt(f.xg) * 42, 0, Math.PI * 2)
    ctx.fillStyle = ehFoco ? 'rgba(79,227,160,.6)' : 'rgba(255,255,255,.38)'
    ctx.fill()
    if (f.gol) { ctx.lineWidth = 5; ctx.strokeStyle = '#ffd166'; ctx.stroke() }
  }
  const resumo = (fs: FinalizacaoNoMapa[]) =>
    `${fs.length} finalizações · ${fs.filter((f) => f.dentroDaArea).length} da área · ${fs.filter((f) => f.noAlvo).length} no alvo`
  const yResumo = JANELA.y + 75 + g.h + 14
  fonte(ctx, 24, 700)
  ctx.textBaseline = 'top'
  ctx.fillStyle = COR.verde
  ctx.fillText(resumo(mapa.finalizacoes.filter((f) => f.clubeId === foco)), JANELA.x + 40, yResumo)
  ctx.fillStyle = COR.texto
  ctx.textAlign = 'right'
  ctx.fillText(resumo(mapa.finalizacoes.filter((f) => f.clubeId === rivalId)), JANELA.x + JANELA.w - 40, yResumo)
  ctx.textAlign = 'left'
  fonte(ctx, 20, 600)
  ctx.fillStyle = COR.suave
  ctx.fillText(`Bolinha maior = chance mais clara · contorno amarelo = gol · dados: ${mapa.fonte}`, JANELA.x + 40, yResumo + 34)

  // Embaixo da janela: onde o time do canal teve a bola
  const calor = mapa.calor.find((t) => t.clubeId === foco)
  if (calor && calor.celulas.length > 0) {
    fonte(ctx, 44, 800)
    ctx.textAlign = 'center'
    ctx.fillStyle = COR.texto
    ctx.fillText(`Onde o ${nomeDe(foco)} teve a bola`, L / 2, JANELA.y + JANELA.h + 30)
    ctx.textAlign = 'left'
    const largura = 860, x0 = (L - largura) / 2, y0 = JANELA.y + JANELA.h + 95
    const k = mancha(ctx, x0, y0, largura, calor.celulas, foco !== p.mandanteId)
    fonte(ctx, 22, 600)
    ctx.fillStyle = COR.suave
    ctx.textAlign = 'center'
    ctx.fillText(`${calor.acoes} ações com a bola · ataca para a direita →`, L / 2, y0 + k.h + 16)
    ctx.textAlign = 'left'
  }
  await baixar(c, `${nome}-4-mapa`)
}
