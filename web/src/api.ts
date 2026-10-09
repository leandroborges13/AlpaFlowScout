// Tipos e chamadas da API do Scout (o Vite repassa /api para a API .NET).

export type Destaque = { nome: string; percentil: number; observacao?: string | null }

export type ResumoAtleta = {
  id: number
  nome: string
  idade: number
  nacionalidade?: string | null
  alturaCm?: number | null
  foto?: string | null
  clube: string
  competicao: string
  temporada: number
  setor: string
  funcao?: string | null
  lado?: string | null
  esquemaMaisComum?: string | null
  jogos: number
  titular: number
  minutos: number
  gols: number
  assistencias: number
  participacaoMinutos: number
  nota?: number | null
  notaEncaixe?: number | null
  destaques: Destaque[]
  experienciaNoEsquema?: string | null
}

export type JogoNaLinhaDoTempo = {
  data: string
  rodada?: string | null
  adversario: string
  emCasa: boolean
  placar?: string | null
  titular: boolean
  funcao?: string | null
}

export type DetalheAtleta = {
  resumo: ResumoAtleta
  escudoClube?: string | null
  funcoes: Record<string, number>
  esquemas: Record<string, number>
  atributos: Destaque[]
  indicadores: { nome: string; percentil: number }[]
  cartoes: { amarelos: number; vermelhos: number; porNoventaMinutos?: number | null }
  presenca: { jogosDoClube: number; comoTitular: number; sequenciaAtual: number; maiorSequencia: number }
  linhaDoTempo: JogoNaLinhaDoTempo[]
  posicoes: { x: number; y: number; jogos: number; funcao: string }[]
  estilo?: { indice: number; rotulo: string; ofensivo: number; defensivo: number; explicacao: string } | null
  trajetoria?: {
    clubes: { clube: string; escudo?: string | null; temporadas: number[] }[]
    transferencias: { data?: string | null; de?: string | null; para?: string | null; tipo?: string | null }[]
  } | null
}

export type Parecidos = { referencia: ResumoAtleta; parecidos: { atleta: ResumoAtleta; semelhanca: number }[] }

export type Mensagem = { papel: 'user' | 'assistant'; texto: string; atletas?: ResumoAtleta[] }

/** Avisa a tela quando o login expira, para voltar à tela de entrada. */
export const EVENTO_SESSAO_EXPIRADA = 'scout:sessao-expirada'

export class ErroDeAcesso extends Error {}

async function pedir<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, { headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin', ...init })
  if (r.status === 401 && !url.endsWith('/login')) {
    window.dispatchEvent(new Event(EVENTO_SESSAO_EXPIRADA))
    throw new ErroDeAcesso('Sua sessão expirou. Entre de novo.')
  }
  if (r.status === 429) throw new Error('Muitas tentativas. Aguarde um minuto e tente de novo.')
  if (!r.ok) {
    const corpo = await r.text()
    let mensagem = corpo
    try { mensagem = JSON.parse(corpo).detail ?? corpo } catch { /* texto simples */ }
    throw new Error(mensagem || `Erro ${r.status}`)
  }
  return r.json() as Promise<T>
}

export const api = {
  sessao: () => pedir<{ nome: string }>('/api/sessao'),
  login: (email: string, senha: string) =>
    pedir<{ nome: string }>('/api/login', { method: 'POST', body: JSON.stringify({ email, senha }) }),
  logout: () => pedir<unknown>('/api/logout', { method: 'POST' }),
  chat: (mensagens: Mensagem[]) =>
    pedir<{ texto: string; atletas: ResumoAtleta[] }>('/api/chat', {
      method: 'POST',
      body: JSON.stringify({ mensagens: mensagens.map(({ papel, texto }) => ({ papel, texto })) }),
    }),
  atleta: (id: number, temporada: number) => pedir<DetalheAtleta>(`/api/atletas/${id}?temporada=${temporada}`),
  parecidos: (id: number, temporada: number) => pedir<Parecidos>(`/api/atletas/${id}/parecidos?temporada=${temporada}`),
  resumoIa: (id: number, temporada: number) => pedir<{ texto: string }>(`/api/atletas/${id}/resumo-ia?temporada=${temporada}`),
}

// Modo gravação: esconde fotos e escudos (direito de imagem e de marca) para vídeos públicos.
const CHAVE_GRAVACAO = 'scout:modo-gravacao'
export function lerModoGravacao(): boolean {
  try { return localStorage.getItem(CHAVE_GRAVACAO) === '1' } catch { return false }
}
export function salvarModoGravacao(ativo: boolean) {
  try { localStorage.setItem(CHAVE_GRAVACAO, ativo ? '1' : '0') } catch { /* sem armazenamento: vale só nesta sessão */ }
}
