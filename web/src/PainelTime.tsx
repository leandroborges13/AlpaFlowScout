import { useEffect, useState } from 'react'
import { api, type DetalheTime, type NecessidadeNaFuncao, type ResumoAtleta, type ResumoTime, type ResumoTreinador } from './api'
import { CartaoAtleta, Medidor } from './Componentes'

/** Escudo do time, ou as iniciais no modo gravação (marca do clube fora de vídeos públicos). */
export function Escudo({ time, grande = false, oculto = false }: { time: Pick<ResumoTime, 'nome' | 'escudo'>; grande?: boolean; oculto?: boolean }) {
  const iniciais = time.nome.split(/\s+/).filter((p) => p.length > 2 || /^[A-Z]/.test(p)).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('')
  return (
    <div className={`retrato escudo ${grande ? 'grande' : ''}`} aria-hidden>
      {time.escudo && !oculto ? <img src={time.escudo} alt="" loading="lazy" /> : <span>{iniciais}</span>}
    </div>
  )
}

type CartaoProps = { time: ResumoTime; gravacao: boolean; onAbrir: () => void; detalhe?: React.ReactNode }

export function CartaoTime({ time: t, gravacao, onAbrir, detalhe }: CartaoProps) {
  return (
    <button className="card" onClick={onAbrir}>
      <div className="card-topo">
        <Escudo time={t} oculto={gravacao} />
        <div className="card-id">
          <div className="card-nome">{t.nome}</div>
          <div className="card-sub">{t.copa ? '' : `${t.posicao}º · `}{t.competicao} {t.temporada}</div>
        </div>
        <NotaDoTime t={t} />
      </div>
      {(t.esquemaMaisUsado || t.estilo) && (
        <span className="funcao">{[t.esquemaMaisUsado, t.estilo].filter(Boolean).join(' · ')}</span>
      )}
      <div className="fatos">
        <span>{t.jogos} jogos</span><span>{t.vitorias}V {t.empates}E {t.derrotas}D</span>
        <span>{t.golsPro} gols pró · {t.golsContra} contra</span>
      </div>
      {detalhe}
      {!detalhe && t.dimensoes.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} />)}
      {!detalhe && t.carencias.length > 0 && <span className="carencia">Carência: {t.carencias.join(', ').toLowerCase()}</span>}
    </button>
  )
}

/** Na liga, os pontos; na copa (mata-mata), o aproveitamento, porque pontos não definem posição. */
function NotaDoTime({ t }: { t: ResumoTime }) {
  return t.copa
    ? <div className="nota"><b>{t.aproveitamento ?? '–'}%</b><span>aprov.</span></div>
    : <div className="nota"><b>{t.pontos}</b><span>pontos</span></div>
}

type PainelProps = {
  time: ResumoTime
  gravacao: boolean
  onFechar: () => void
  onAbrirAtleta: (a: ResumoAtleta) => void
  onAbrirTreinador: (t: ResumoTreinador) => void
}

export function PainelTime({ time, gravacao, onFechar, onAbrirAtleta, onAbrirTreinador }: PainelProps) {
  const [detalhe, setDetalhe] = useState<DetalheTime | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setDetalhe(null); setErro(null)
    api.time(time.id, time.competicaoId, time.temporada).then(setDetalhe).catch((e) => setErro(e.message))
  }, [time])

  useEffect(() => {
    const fechar = (e: KeyboardEvent) => { if (e.key === 'Escape') onFechar() }
    window.addEventListener('keydown', fechar)
    return () => window.removeEventListener('keydown', fechar)
  }, [onFechar])

  const t = detalhe?.resumo ?? time
  // Funções em ordem de campo: defesa, meio, ataque (goleiro fica de fora, a nota dele mede outra coisa)
  const funcoes = (detalhe?.funcoes ?? []).filter((f) => f.setor !== 'Goleiro')

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel" role="dialog" aria-modal aria-label={t.nome} onClick={(e) => e.stopPropagation()}>
        <div className="perfil-topo">
          <Escudo time={t} grande oculto={gravacao} />
          <div className="perfil-id">
            <h2>{t.nome}</h2>
            <div className="card-sub">{t.competicao} {t.temporada}{t.copa ? '' : ` · ${t.posicao}º colocado`}</div>
            {t.esquemaMaisUsado && <span className="funcao">Esquema mais usado: {t.esquemaMaisUsado}</span>}
          </div>
          <NotaDoTime t={t} />
        </div>

        <div className="tiles">
          <Tile valor={`${t.vitorias}V ${t.empates}E ${t.derrotas}D`} rotulo={`em ${t.jogos} jogos`} />
          <Tile valor={t.aproveitamento != null ? `${t.aproveitamento}%` : '—'} rotulo="aproveitamento" />
          <Tile valor={`${t.golsPro} : ${t.golsContra}`} rotulo="gols pró e contra" />
        </div>

        {erro && <p className="erro">{erro}</p>}
        {!detalhe && !erro && <p className="pensando">Carregando análise do time…</p>}

        {detalhe && (
          <>
            {detalhe.estilo && (
              <section className="resumo-ia">
                <h3>Estilo de jogo</h3>
                <p><b>{detalhe.estilo.rotulo}.</b> {detalhe.estilo.explicacao}</p>
              </section>
            )}

            {detalhe.dimensoes.length > 0 && (
              <section>
                <h3>Pontos fortes e fracos</h3>
                <p className="legenda">Percentil entre os times da competição. A linha marca 50, o time mediano.</p>
                <div className="grade2">
                  {detalhe.dimensoes.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} mediana />)}
                </div>
                <details className="transferencias">
                  <summary>Ver os {detalhe.indicadores.length} indicadores</summary>
                  <div className="grade2" style={{ marginTop: 10 }}>
                    {detalhe.indicadores.map((i) => <Medidor key={i.nome} nome={i.nome} valor={i.percentil} mediana />)}
                  </div>
                </details>
              </section>
            )}

            {detalhe.esquemas.length > 0 && (
              <section>
                <h3>Esquemas</h3>
                <p className="legenda">Esquema inicial de cada jogo, a partir das escalações. Aproveitamento = pontos ganhos com aquele esquema.</p>
                <ListaDeEsquemas esquemas={detalhe.esquemas} />
              </section>
            )}

            {detalhe.treinadores.length > 0 && (
              <section>
                <h3>Treinadores na temporada</h3>
                <ul className="treinadores-time">
                  {detalhe.treinadores.map((tr) => (
                    <li key={tr.id}>
                      <button className="link" onClick={() => onAbrirTreinador(tr)}>{tr.nome}</button>
                      {tr.noCargo && <span className="selo-cargo">no cargo</span>}
                      <span className="card-sub">
                        {Periodo(tr.inicio, tr.fim, tr.noCargo)} · {tr.jogos} {tr.jogos === 1 ? 'jogo' : 'jogos'} · {tr.vitorias}V {tr.empates}E {tr.derrotas}D
                        {tr.aproveitamento != null ? ` · ${tr.aproveitamento}%` : ''}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {funcoes.length > 0 && (
              <section>
                <h3>Como está servido em cada função</h3>
                <p className="legenda">Nota dos titulares de cada função, comparada com a dos outros times que usam a mesma função. Abaixo de 35 é carência.</p>
                <ul className="funcoes-time">
                  {funcoes.map((f) => <LinhaFuncao key={f.funcao} f={f} />)}
                </ul>
              </section>
            )}

            <section>
              <h3>Elenco</h3>
              <div className="tiles">
                <Tile valor={detalhe.elenco.idadeMedia != null ? detalhe.elenco.idadeMedia.toLocaleString('pt-BR') : '—'} rotulo="idade média em campo" />
                <Tile valor={detalhe.elenco.percentualMinutosAte23 != null ? `${detalhe.elenco.percentualMinutosAte23}%` : '—'} rotulo="dos minutos para até 23 anos" />
                <Tile valor={String(detalhe.elenco.atletasUsados)} rotulo="atletas usados" />
                <Tile valor={detalhe.elenco.rodizio ?? '—'} rotulo="rodízio" />
              </div>
              <p className="legenda">{detalhe.elenco.explicacao}</p>
            </section>

            {detalhe.principais.length > 0 && (
              <section>
                <h3>Quem mais joga</h3>
                <div className="cards">
                  {detalhe.principais.map((a) => <CartaoAtleta key={a.id} atleta={a} gravacao={gravacao} onAbrir={() => onAbrirAtleta(a)} />)}
                </div>
              </section>
            )}

            {detalhe.observacoes.map((o) => <p key={o} className="obs">{o}</p>)}
          </>
        )}

        <div className="acoes">
          <button className="btn prim" onClick={onFechar}>Fechar</button>
        </div>
      </div>
    </div>
  )
}

function LinhaFuncao({ f }: { f: NecessidadeNaFuncao }) {
  return (
    <li className={f.carencia ? 'carente' : ''}>
      <div className="funcao-topo">
        <b>{f.funcao}</b>
        {f.carencia && <span className="selo-carencia">carência</span>}
        <span className="card-sub">{f.jogos} jogos{f.porJogo > 1 ? ` · ${f.porJogo} por jogo` : ''}{f.notaTitulares != null ? ` · nota dos titulares ${f.notaTitulares}` : ''}</span>
      </div>
      {f.percentil != null
        ? <Medidor nome="Em relação aos outros times" valor={f.percentil} mediana />
        : <span className="card-sub">Poucos times com esta função ou titulares sem nota para comparar.</span>}
      {f.titulares.length > 0 && (
        <span className="card-sub">{f.titulares.map((a) => `${a.nome} (${a.jogos})`).join(' · ')}</span>
      )}
    </li>
  )
}

function Tile({ valor, rotulo }: { valor: string; rotulo: string }) {
  return <div className="tile"><b>{valor}</b><span>{rotulo}</span></div>
}

/** Barras por esquema, separando jogos em casa e fora, com o aproveitamento de cada um. */
export function ListaDeEsquemas({ esquemas }: { esquemas: DetalheTime['esquemas'] }) {
  const maxJogos = Math.max(1, ...esquemas.map((e) => e.jogos))
  return (
    <>
      <div className="esquemas">
        {esquemas.map((e) => (
          <div key={e.esquema} className="esquema">
            <b>{e.esquema}</b>
            <div className="esquema-barra" role="img" aria-label={`${e.jogos} jogos: ${e.emCasa} em casa e ${e.fora} fora`}>
              <i className="casa" style={{ width: `${(e.emCasa / maxJogos) * 100}%` }} />
              <i className="fora" style={{ width: `${(e.fora / maxJogos) * 100}%` }} />
            </div>
            <span className="card-sub">{e.jogos} {e.jogos === 1 ? 'jogo' : 'jogos'} · {e.aproveitamento}% de aproveitamento</span>
          </div>
        ))}
      </div>
      <div className="lt-legenda"><span><i className="lt-quad casa" /> em casa</span><span><i className="lt-quad fora" /> fora</span></div>
    </>
  )
}

/** "12/03 a 20/07" ou "desde 12/03". */
export function Periodo(inicio: string, fim: string, noCargo: boolean) {
  const d = (s: string) => new Date(s).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })
  return noCargo ? `desde ${d(inicio)}` : `${d(inicio)} a ${d(fim)}`
}
