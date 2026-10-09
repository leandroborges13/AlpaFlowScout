import type { ResumoAtleta } from './api'

/** Barra de percentil. Com `mediana`, marca o 50 (atleta mediano do setor) como referência. */
export function Medidor({ nome, valor, mediana = false }: { nome: string; valor: number; mediana?: boolean }) {
  return (
    <div className="medidor" title={`Melhor que ${Math.round(valor)}% dos atletas do mesmo setor`}>
      <span>{nome}</span>
      <b>{Math.round(valor)}</b>
      <div className="trilho">
        <i style={{ width: `${Math.max(2, valor)}%` }} />
        {mediana && <em className="mediana" aria-hidden />}
      </div>
    </div>
  )
}

/** Foto do atleta, ou as iniciais quando não há foto ou no modo gravação. */
export function Retrato({ foto, nome, grande = false, oculto = false }: { foto?: string | null; nome: string; grande?: boolean; oculto?: boolean }) {
  const iniciais = nome.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('')
  return (
    <div className={`retrato ${grande ? 'grande' : ''}`} aria-hidden>
      {foto && !oculto ? <img src={foto} alt="" loading="lazy" /> : <span>{iniciais}</span>}
    </div>
  )
}

type CartaoProps = { atleta: ResumoAtleta; onAbrir: () => void; semelhanca?: number; gravacao: boolean }

export function CartaoAtleta({ atleta: a, onAbrir, semelhanca, gravacao }: CartaoProps) {
  const nota = semelhanca ?? a.notaEncaixe ?? a.nota
  const rotulo = semelhanca != null ? 'semelhança' : a.notaEncaixe != null ? 'encaixe' : 'nota'
  return (
    <button className="card" onClick={onAbrir}>
      <div className="card-topo">
        <Retrato foto={a.foto} nome={a.nome} oculto={gravacao} />
        <div className="card-id">
          <div className="card-nome">{a.nome}</div>
          <div className="card-sub">{a.idade} anos · {a.clube} · {a.competicao}</div>
        </div>
        {nota != null && (
          <div className="nota"><b>{Math.round(nota)}</b><span>{rotulo}</span></div>
        )}
      </div>
      {a.funcao && <span className="funcao">{a.funcao}{a.esquemaMaisComum ? ` · ${a.esquemaMaisComum}` : ''}</span>}
      {a.experienciaNoEsquema && <span className="card-sub">{a.experienciaNoEsquema}</span>}
      <div className="fatos">
        <span>{a.jogos} jogos</span><span>{a.minutos.toLocaleString('pt-BR')} min</span>
        <span>{a.participacaoMinutos}% dos minutos</span>{a.gols > 0 && <span>{a.gols} {a.gols === 1 ? 'gol' : 'gols'}</span>}
      </div>
      {a.destaques.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} />)}
    </button>
  )
}
