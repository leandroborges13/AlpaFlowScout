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

export type DetalheAtleta = {
  resumo: ResumoAtleta
  funcoes: Record<string, number>
  esquemas: Record<string, number>
  atributos: Destaque[]
  indicadores: { nome: string; percentil: number }[]
}

export type Parecidos = { referencia: ResumoAtleta; parecidos: { atleta: ResumoAtleta; semelhanca: number }[] }

export type Mensagem = { papel: 'user' | 'assistant'; texto: string; atletas?: ResumoAtleta[] }

async function pedir<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...init })
  if (!r.ok) throw new Error((await r.text()) || `Erro ${r.status}`)
  return r.json() as Promise<T>
}

export const api = {
  chat: (mensagens: Mensagem[]) =>
    pedir<{ texto: string; atletas: ResumoAtleta[] }>('/api/chat', {
      method: 'POST',
      body: JSON.stringify({ mensagens: mensagens.map(({ papel, texto }) => ({ papel, texto })) }),
    }),
  atleta: (id: number, temporada: number) => pedir<DetalheAtleta>(`/api/atletas/${id}?temporada=${temporada}`),
  parecidos: (id: number, temporada: number) => pedir<Parecidos>(`/api/atletas/${id}/parecidos?temporada=${temporada}`),
}
