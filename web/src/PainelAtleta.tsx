import { useEffect, useState } from 'react'
import { api, type DetalheAtleta, type JogoNaLinhaDoTempo, type Parecidos, type ResumoAtleta, type ResumoTime, type TimesParaAtleta } from './api'
import { BarraDeEstilo, CampoDePosicoes } from './Campo'
import { CartaoAtleta, Medidor, Retrato } from './Componentes'
import { CartaoTime } from './PainelTime'

type Props = {
  atleta: ResumoAtleta
  gravacao: boolean
  onFechar: () => void
  onAbrir: (a: ResumoAtleta) => void
  onAbrirTime: (t: ResumoTime) => void
}

export function PainelAtleta({ atleta, gravacao, onFechar, onAbrir, onAbrirTime }: Props) {
  const [detalhe, setDetalhe] = useState<DetalheAtleta | null>(null)
  const [resumoIa, setResumoIa] = useState<string | null>(null)
  const [parecidos, setParecidos] = useState<Parecidos | null>(null)
  const [encaixes, setEncaixes] = useState<TimesParaAtleta | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setDetalhe(null); setResumoIa(null); setParecidos(null); setEncaixes(null); setErro(null)
    api.atleta(atleta.id, atleta.temporada).then(setDetalhe).catch((e) => setErro(e.message))
    api.resumoIa(atleta.id, atleta.temporada).then((r) => setResumoIa(r.texto)).catch(() => setResumoIa(''))
  }, [atleta])

  useEffect(() => {
    const fechar = (e: KeyboardEvent) => { if (e.key === 'Escape') onFechar() }
    window.addEventListener('keydown', fechar)
    return () => window.removeEventListener('keydown', fechar)
  }, [onFechar])

  const a = detalhe?.resumo ?? atleta
  const atributos = [...(detalhe?.atributos ?? [])].sort((x, y) => y.percentil - x.percentil)
  const observacoes = [...new Set(atributos.map((x) => x.observacao).filter(Boolean))]

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel" role="dialog" aria-modal aria-label={a.nome} onClick={(e) => e.stopPropagation()}>
        <div className="perfil-topo">
          <Retrato foto={a.foto} nome={a.nome} grande oculto={gravacao} />
          <div className="perfil-id">
            <h2>{a.nome}</h2>
            <div className="card-sub">
              {a.idade} anos{a.alturaCm ? ` · ${a.alturaCm} cm` : ''}{a.nacionalidade ? ` · ${a.nacionalidade}` : ''}
            </div>
            <div className="clube-linha">
              {!gravacao && detalhe?.escudoClube && <img src={detalhe.escudoClube} alt="" width={20} height={20} />}
              <span>{a.clube} · {a.competicao} {a.temporada}</span>
            </div>
            {a.funcao && <span className="funcao">{a.funcao}{a.esquemaMaisComum ? ` · ${a.esquemaMaisComum}` : ''}</span>}
          </div>
          {a.nota != null && <div className="nota"><b>{Math.round(a.nota)}</b><span>nota geral</span></div>}
        </div>

        <section className="resumo-ia">
          <h3>Resumo do analista</h3>
          {resumoIa === null && <p className="pensando">Escrevendo o resumo…</p>}
          {resumoIa && <p>{resumoIa}</p>}
          {resumoIa === '' && <p className="card-sub">Resumo indisponível agora.</p>}
          {resumoIa && <small className="card-sub">Gerado por IA a partir das métricas da AlpaFlow Scout.</small>}
        </section>

        {erro && <p className="erro">{erro}</p>}
        {!detalhe && !erro && <p className="pensando">Carregando perfil…</p>}

        {detalhe && (
          <>
            <section>
              <h3>Posições e forma de jogo</h3>
              <div className="posicoes">
                <CampoDePosicoes posicoes={detalhe.posicoes ?? []} />
                {detalhe.estilo && <BarraDeEstilo estilo={detalhe.estilo} />}
              </div>
            </section>

            <section>
              <h3>Perfil de atributos</h3>
              <p className="legenda">Percentil entre atletas do mesmo setor na competição. A linha marca 50, o atleta mediano.</p>
              <div className="grade2">
                {atributos.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} mediana />)}
              </div>
              {observacoes.map((o) => <p key={o} className="obs">{o}</p>)}
            </section>

            <section>
              <h3>Regularidade</h3>
              <div className="tiles">
                <Tile valor={`${detalhe.presenca.comoTitular}/${detalhe.presenca.jogosDoClube}`} rotulo="jogos como titular" />
                <Tile valor={`${a.participacaoMinutos}%`} rotulo="dos minutos do clube" />
                <Tile valor={String(detalhe.presenca.sequenciaAtual)} rotulo="titular seguidas (atual)" />
                <Tile valor={String(detalhe.presenca.maiorSequencia)} rotulo="maior sequência" />
              </div>
              <LinhaDoTempo jogos={detalhe.linhaDoTempo} />
            </section>

            {detalhe.copas && detalhe.copas.length > 0 && (
              <section>
                <h3>Desempenho em copas</h3>
                <p className="legenda">Separado da nota da liga. A nota na copa só aparece com pelo menos 450 minutos jogados nela.</p>
                <ul className="copas">
                  {detalhe.copas.map((c) => (
                    <li key={`${c.competicaoId}-${c.temporada}`}>
                      <div className="funcao-topo">
                        <b>{c.competicao} {c.temporada}</b>
                        <span className="card-sub">
                          {c.jogos} {c.jogos === 1 ? 'jogo' : 'jogos'} · {c.minutos.toLocaleString('pt-BR')} min
                          {c.gols > 0 ? ` · ${c.gols} ${c.gols === 1 ? 'gol' : 'gols'}` : ''}
                          {c.assistencias > 0 ? ` · ${c.assistencias} ${c.assistencias === 1 ? 'assistência' : 'assistências'}` : ''}
                        </span>
                        {c.nota != null && <span className="selo-cargo">nota {Math.round(c.nota)}</span>}
                      </div>
                      {c.destaques.length > 0 && (
                        <div className="grade2">{c.destaques.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} mediana />)}</div>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}

            <section>
              <h3>Disciplina</h3>
              <div className="tiles">
                <Tile valor={String(detalhe.cartoes.amarelos)} rotulo="cartões amarelos" />
                <Tile valor={String(detalhe.cartoes.vermelhos)} rotulo="cartões vermelhos" />
                <Tile valor={detalhe.cartoes.porNoventaMinutos?.toLocaleString('pt-BR') ?? '—'} rotulo="cartões a cada 90 min" />
              </div>
            </section>

            {Object.keys(detalhe.funcoes).length > 0 && (
              <section>
                <h3>Funções e esquemas (jogos como titular)</h3>
                <div className="chips">
                  {Object.entries(detalhe.funcoes).sort((x, y) => y[1] - x[1]).map(([f, n]) => <span key={f}>{f}: {n}</span>)}
                </div>
                <div className="chips" style={{ marginTop: 6 }}>
                  {Object.entries(detalhe.esquemas).sort((x, y) => y[1] - x[1]).map(([f, n]) => <span key={f}>{f}: {n}</span>)}
                </div>
              </section>
            )}

            {detalhe.trajetoria && detalhe.trajetoria.clubes.length > 0 && (
              <section>
                <h3>Trajetória</h3>
                <ol className="trajetoria">
                  {detalhe.trajetoria.clubes.map((c) => (
                    <li key={c.clube}>
                      {!gravacao && c.escudo ? <img src={c.escudo} alt="" width={22} height={22} /> : <i aria-hidden className="ponto" />}
                      <span className="t-clube">{c.clube}</span>
                      <span className="card-sub">{Faixa(c.temporadas)}</span>
                    </li>
                  ))}
                </ol>
                {detalhe.trajetoria.transferencias.length > 0 && (
                  <details className="transferencias">
                    <summary>{detalhe.trajetoria.transferencias.length} transferências registradas</summary>
                    <ul>
                      {detalhe.trajetoria.transferencias.map((t, i) => (
                        <li key={i}>
                          <span className="card-sub">{t.data ? new Date(t.data + 'T12:00').toLocaleDateString('pt-BR') : '—'}</span>
                          {' '}{t.de ?? '?'} → {t.para ?? '?'}{t.tipo && t.tipo !== 'N/A' ? ` (${t.tipo})` : ''}
                        </li>
                      ))}
                    </ul>
                  </details>
                )}
              </section>
            )}
          </>
        )}

        {parecidos && (
          <section>
            <h3>Perfis parecidos</h3>
            <div className="cards">
              {parecidos.parecidos.map((p) => (
                <CartaoAtleta key={p.atleta.id} atleta={p.atleta} semelhanca={p.semelhanca} gravacao={gravacao} onAbrir={() => onAbrir(p.atleta)} />
              ))}
            </div>
          </section>
        )}

        {encaixes && (
          <section>
            <h3>Onde encaixaria</h3>
            <p className="legenda">Times da mesma competição que usam a função dele e têm ali titulares com nota menor. Ganho = diferença de nota.</p>
            {encaixes.times.length === 0 && <p className="card-sub">Nenhum time da competição com titulares abaixo dele nessa função.</p>}
            <div className="cards">
              {encaixes.times.map((e) => (
                <CartaoTime key={e.time.id} time={e.time} gravacao={gravacao} onAbrir={() => onAbrirTime(e.time)}
                  detalhe={(
                    <div className="encaixe">
                      <span>{e.funcao}: titulares com nota <b>{e.notaTitulares ?? '—'}</b> · ele <b>{e.notaAtleta}</b> · ganho <b>+{e.ganho}</b></span>
                      {e.experienciaNoEsquema && <span>{e.experienciaNoEsquema}</span>}
                      {e.titularesAtuais.length > 0 && <span>Hoje: {e.titularesAtuais.map((t) => t.nome).join(', ')}</span>}
                    </div>
                  )} />
              ))}
            </div>
          </section>
        )}

        <div className="acoes">
          {!encaixes && (
            <button className="btn" onClick={() => api.timesParaAtleta(atleta.id, atleta.temporada).then(setEncaixes).catch((e) => setErro(e.message))}>
              Onde ele encaixa
            </button>
          )}
          {!parecidos && (
            <button className="btn" onClick={() => api.parecidos(atleta.id, atleta.temporada).then(setParecidos).catch((e) => setErro(e.message))}>
              Ver parecidos
            </button>
          )}
          <button className="btn prim" onClick={onFechar}>Fechar</button>
        </div>
      </div>
    </div>
  )
}

function Tile({ valor, rotulo }: { valor: string; rotulo: string }) {
  return <div className="tile"><b>{valor}</b><span>{rotulo}</span></div>
}

/** "2022–2026" para temporadas seguidas; "2019, 2021" quando há buracos. */
function Faixa(temporadas: number[]) {
  const t = [...temporadas].sort((a, b) => a - b)
  if (t.length === 0) return 'ano não informado'
  const seguidas = t.every((v, i) => i === 0 || v === t[i - 1] + 1)
  return seguidas && t.length > 1 ? `${t[0]}–${t[t.length - 1]}` : t.join(', ')
}

/** Um quadrado por jogo do clube: cheio quando o atleta foi titular. */
function LinhaDoTempo({ jogos }: { jogos: JogoNaLinhaDoTempo[] }) {
  const [foco, setFoco] = useState<JogoNaLinhaDoTempo | null>(null)
  if (jogos.length === 0) return null
  const descrever = (j: JogoNaLinhaDoTempo) =>
    `${new Date(j.data).toLocaleDateString('pt-BR')} · ${j.emCasa ? 'em casa' : 'fora'} contra ${j.adversario}${j.placar ? ` (${j.placar})` : ''} · ${j.titular ? `titular${j.funcao ? ` como ${j.funcao.toLowerCase()}` : ''}` : 'não foi titular'}`
  return (
    <div className="linha-tempo">
      <div className="lt-legenda">
        <span><i className="lt-quad titular" /> titular</span>
        <span><i className="lt-quad" /> não titular</span>
        <span className="card-sub">{jogos.length} jogos do clube, em ordem</span>
      </div>
      <div className="lt-faixa" onMouseLeave={() => setFoco(null)}>
        {jogos.map((j, i) => (
          <button
            key={i}
            className={`lt-quad ${j.titular ? 'titular' : ''}`}
            aria-label={descrever(j)}
            onMouseEnter={() => setFoco(j)}
            onFocus={() => setFoco(j)}
            onBlur={() => setFoco(null)}
          />
        ))}
      </div>
      <div className="lt-dica" aria-live="polite">{foco ? descrever(foco) : 'Passe o mouse num jogo para ver o detalhe.'}</div>
    </div>
  )
}
