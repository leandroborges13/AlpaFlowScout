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
  copas?: { competicaoId: number; competicao: string; temporada: number; jogos: number; minutos: number; gols: number; assistencias: number; nota?: number | null; destaques: Destaque[] }[]
  trajetoria?: {
    clubes: { clube: string; escudo?: string | null; temporadas: number[] }[]
    transferencias: { data?: string | null; de?: string | null; para?: string | null; tipo?: string | null }[]
  } | null
}

export type Parecidos = { referencia: ResumoAtleta; parecidos: { atleta: ResumoAtleta; semelhanca: number }[] }

export type ResumoTime = {
  id: number
  nome: string
  escudo?: string | null
  competicaoId: number
  competicao: string
  temporada: number
  posicao?: number | null
  jogos: number
  vitorias: number
  empates: number
  derrotas: number
  golsPro: number
  golsContra: number
  pontos: number
  aproveitamento?: number | null
  esquemaMaisUsado?: string | null
  estilo?: string | null
  dimensoes: Destaque[]
  carencias: string[]
  copa?: boolean
}

export type TitularNaFuncao = { id: number; nome: string; jogos: number; nota?: number | null }

export type NecessidadeNaFuncao = {
  funcao: string
  setor: string
  jogos: number
  porJogo: number
  notaTitulares?: number | null
  percentil?: number | null
  carencia: boolean
  titulares: TitularNaFuncao[]
}

export type DetalheTime = {
  resumo: ResumoTime
  esquemas: { esquema: string; jogos: number; emCasa: number; fora: number; aproveitamento?: number | null }[]
  dimensoes: Destaque[]
  indicadores: { nome: string; percentil: number }[]
  estilo?: { rotulo: string; percentilPosse?: number | null; explicacao: string } | null
  elenco: { idadeMedia?: number | null; percentualMinutosAte23?: number | null; atletasUsados: number; rodizio?: string | null; explicacao: string }
  funcoes: NecessidadeNaFuncao[]
  principais: ResumoAtleta[]
  treinadores: ResumoTreinador[]
  observacoes: string[]
}

export type EncaixeEmTime = {
  time: ResumoTime
  funcao: string
  notaTitulares?: number | null
  notaAtleta: number
  ganho: number
  experienciaNoEsquema?: string | null
  titularesAtuais: TitularNaFuncao[]
}

export type TimesParaAtleta = { atleta: ResumoAtleta; times: EncaixeEmTime[]; observacoes: string[] }

export type Mensagem = { papel: 'user' | 'assistant'; texto: string; atletas?: ResumoAtleta[]; times?: ResumoTime[]; treinadores?: ResumoTreinador[]; partidas?: ResumoPartida[] }

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
    pedir<{ texto: string; atletas: ResumoAtleta[]; times: ResumoTime[]; treinadores: ResumoTreinador[]; partidas: ResumoPartida[] }>('/api/chat', {
      method: 'POST',
      body: JSON.stringify({ mensagens: mensagens.map(({ papel, texto }) => ({ papel, texto })) }),
    }),
  atleta: (id: number, temporada: number) => pedir<DetalheAtleta>(`/api/atletas/${id}?temporada=${temporada}`),
  parecidos: (id: number, temporada: number) => pedir<Parecidos>(`/api/atletas/${id}/parecidos?temporada=${temporada}`),
  resumoIa: (id: number, temporada: number) => pedir<{ texto: string }>(`/api/atletas/${id}/resumo-ia?temporada=${temporada}`),
  time: (id: number, competicao: number, temporada: number) =>
    pedir<DetalheTime>(`/api/times/${id}?competicao=${competicao}&temporada=${temporada}`),
  timesParaAtleta: (id: number, temporada: number) => pedir<TimesParaAtleta>(`/api/atletas/${id}/times?temporada=${temporada}`),
  treinador: (id: number) => pedir<DetalheTreinador>(`/api/treinadores/${id}`),
  atletasDoTreinador: (id: number) => pedir<AtletaDoTreinador[]>(`/api/treinadores/${id}/atletas`),
  sugestoesParaTreinador: (id: number) => pedir<SugestoesParaTreinador>(`/api/treinadores/${id}/sugestoes`),
  partida: (id: number) => pedir<AnalisePartida>(`/api/partidas/${id}`),
}

// Modo gravação: esconde fotos e escudos (direito de imagem e de marca) para vídeos públicos.
const CHAVE_GRAVACAO = 'scout:modo-gravacao'
export function lerModoGravacao(): boolean {
  try { return localStorage.getItem(CHAVE_GRAVACAO) === '1' } catch { return false }
}
export function salvarModoGravacao(ativo: boolean) {
  try { localStorage.setItem(CHAVE_GRAVACAO, ativo ? '1' : '0') } catch { /* sem armazenamento: vale só nesta sessão */ }
}

export type ResumoTreinador = {
  id: number
  nome: string
  foto?: string | null
  clubeId: number
  clube: string
  escudo?: string | null
  competicaoId: number
  competicao: string
  temporada: number
  noCargo: boolean
  inicio: string
  fim: string
  jogos: number
  vitorias: number
  empates: number
  derrotas: number
  aproveitamento?: number | null
  esquemaPreferido?: string | null
  estilo?: string | null
  dimensoes: Destaque[]
  titularesAte23?: number | null
  copa?: boolean
}

export type PassagemDetalhada = {
  passagem: ResumoTreinador
  esquemas: DetalheTime['esquemas']
  indicadores: { nome: string; percentil: number }[]
  estilo?: DetalheTime['estilo']
  idadeMediaTitulares?: number | null
  percentilJovens?: number | null
  noMesmoClube: { id: number; nome: string; inicio: string; fim: string; jogos: number; aproveitamento?: number | null }[]
  comparacao?: string | null
}

export type DetalheTreinador = {
  resumo: ResumoTreinador
  passagens: PassagemDetalhada[]
  carreira?: { idade?: number | null; nacionalidade?: string | null; passagens: { clube: string; escudo?: string | null; inicio?: string | null; fim?: string | null }[] } | null
  observacoes: string[]
}

export type ResumoPartida = {
  id: number
  data: string
  rodada?: string | null
  competicaoId: number
  competicao: string
  temporada: number
  status: string
  mandanteId: number
  mandante: string
  escudoMandante?: string | null
  visitanteId: number
  visitante: string
  escudoVisitante?: string | null
  golsMandante?: number | null
  golsVisitante?: number | null
}

export type LadoDaPartida = {
  clubeId: number
  time: string
  esquema?: string | null
  treinadorId?: number | null
  treinador?: string | null
  titulares: { id?: number | null; nome: string; funcao?: string | null }[]
  reservas: { id?: number | null; nome: string }[]
}

export type EventoDaPartida = {
  minuto: number
  acrescimo?: number | null
  clubeId: number
  tipo: string
  jogador?: string | null
  outro?: string | null
  detalhe?: string | null
}

export type AnalisePartida = {
  partida: ResumoPartida
  mandante: LadoDaPartida
  visitante: LadoDaPartida
  eventos: EventoDaPartida[]
  aspectos: { aspecto: string; vantagem?: string | null; percentilMandante?: number | null; percentilVisitante?: number | null }[]
  observacoes: string[]
  desfecho?: string | null
}

export type AtletaDoTreinador = { atleta: ResumoAtleta; jogosComEle: number; funcaoComEle?: string | null }

export type SugestoesParaTreinador = {
  treinador: ResumoTreinador
  funcoes: {
    funcao: string
    jogos: number
    notaTitulares?: number | null
    percentilNaCompeticao?: number | null
    perfil: Destaque[]
    quemEleEscala: string[]
    sugestoes: { atleta: ResumoAtleta; semelhanca: number }[]
  }[]
  observacoes: string[]
}
