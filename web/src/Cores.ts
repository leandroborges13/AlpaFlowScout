// Cores dos clubes para os cartões do "Para vídeo" e a prancheta (cor sozinha não é marca protegida).
// camisa = cor principal do uniforme, detalhe = a segunda cor, destaque = a cor que aparece bem no fundo verde-escuro dos cartões
// (para clube de camisa preta ou branca, o destaque é o branco).

export type CoresDoClube = { camisa: string; detalhe: string; destaque: string }

const BRANCO = '#f4f4f4'
const PADRAO: CoresDoClube = { camisa: '#4fe3a0', detalhe: '#07170f', destaque: '#4fe3a0' }

// chave = trecho do nome sem acento, minúsculo (como a API-Football escreve: "Atletico-MG", "Vasco DA Gama", "Sport Recife")
const TABELA: [string, CoresDoClube][] = [
  ['atletico-mg', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['atletico mineiro', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['atletico paranaense', { camisa: '#c8102e', detalhe: '#111111', destaque: '#ff4757' }],
  ['athletico', { camisa: '#c8102e', detalhe: '#111111', destaque: '#ff4757' }],
  ['atletico-go', { camisa: '#c8102e', detalhe: '#111111', destaque: '#ff4757' }],
  ['gremio', { camisa: '#0d80bf', detalhe: '#111111', destaque: '#3fb0f5' }],
  ['flamengo', { camisa: '#c4161c', detalhe: '#111111', destaque: '#ff4048' }],
  ['palmeiras', { camisa: '#006437', detalhe: '#ffffff', destaque: '#2fd17f' }],
  ['corinthians', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['sao paulo', { camisa: '#ffffff', detalhe: '#d71920', destaque: '#ff4a4a' }],
  ['santos', { camisa: '#ffffff', detalhe: '#111111', destaque: BRANCO }],
  ['internacional', { camisa: '#d50b14', detalhe: '#ffffff', destaque: '#ff4048' }],
  ['fluminense', { camisa: '#7a1e3a', detalhe: '#00613c', destaque: '#e0527c' }],
  ['botafogo', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['vasco', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['cruzeiro', { camisa: '#0033a0', detalhe: '#ffffff', destaque: '#4d8bff' }],
  ['bahia', { camisa: '#0057a8', detalhe: '#d71920', destaque: '#4d94ff' }],
  ['fortaleza', { camisa: '#0b3f8f', detalhe: '#d71920', destaque: '#5c8fe6' }],
  ['bragantino', { camisa: '#ffffff', detalhe: '#d71920', destaque: '#ff4a4a' }],
  ['vitoria', { camisa: '#d71920', detalhe: '#111111', destaque: '#ff4a4a' }],
  ['sport', { camisa: '#d71920', detalhe: '#111111', destaque: '#ff4a4a' }],
  ['ceara', { camisa: '#111111', detalhe: '#ffffff', destaque: BRANCO }],
  ['juventude', { camisa: '#00813e', detalhe: '#ffffff', destaque: '#2fd17f' }],
  ['mirassol', { camisa: '#ffd200', detalhe: '#00813e', destaque: '#ffd200' }],
  ['coritiba', { camisa: '#00613c', detalhe: '#ffffff', destaque: '#2fd17f' }],
  ['chapecoense', { camisa: '#00813e', detalhe: '#ffffff', destaque: '#2fd17f' }],
  ['remo', { camisa: '#14286e', detalhe: '#ffffff', destaque: '#5c7fe6' }],
  ['paysandu', { camisa: '#4fb3ff', detalhe: '#ffffff', destaque: '#6cc2ff' }],
  ['goias', { camisa: '#00613c', detalhe: '#ffffff', destaque: '#2fd17f' }],
  ['cuiaba', { camisa: '#ffd200', detalhe: '#00613c', destaque: '#ffd200' }],
  ['america-mg', { camisa: '#00813e', detalhe: '#111111', destaque: '#2fd17f' }],
  ['america mineiro', { camisa: '#00813e', detalhe: '#111111', destaque: '#2fd17f' }],
]

const normal = (nome: string) => nome.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** Cores do clube pelo nome; clube fora da tabela fica com o verde do canal. */
export function coresDoClube(nome: string | null | undefined): CoresDoClube {
  if (!nome) return PADRAO
  const n = normal(nome)
  return TABELA.find(([chave]) => n.includes(chave))?.[1] ?? PADRAO
}

function rgb(hex: string) {
  const h = hex.replace('#', '')
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16))
}

/** Cor com transparência (para bolinhas e manchas por cima do campo). */
export function comAlfa(hex: string, alfa: number) {
  const [r, g, b] = rgb(hex)
  return `rgba(${r}, ${g}, ${b}, ${alfa})`
}

const CINZA_RIVAL = '#8a9a92'

/**
 * Destaques dos dois times de um jogo. Quando as cores ficam parecidas (Galo x Botafogo, os dois de branco), o segundo time
 * fica com o cinza neutro, para o vídeo não confundir quem é quem.
 */
export function coresDoJogo(time1: string, time2: string): [string, string] {
  const a = coresDoClube(time1).destaque
  const b = coresDoClube(time2).destaque
  const [r1, g1, b1] = rgb(a), [r2, g2, b2] = rgb(b)
  const distancia = Math.hypot(r1 - r2, g1 - g2, b1 - b2)
  return [a, distancia < 90 ? CINZA_RIVAL : b]
}
