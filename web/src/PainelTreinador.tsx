import { useEffect, useState } from 'react'
import { api, type AtletaDoTreinador, type DetalheTreinador, type PassagemDetalhada, type ResumoAtleta, type ResumoTreinador, type SugestoesParaTreinador } from './api'
import { CartaoAtleta, Medidor, Retrato } from './Componentes'
import { Escudo, ListaDeEsquemas, Periodo } from './PainelTime'

type CartaoProps = { treinador: ResumoTreinador; gravacao: boolean; onAbrir: () => void }

export function CartaoTreinador({ treinador: t, gravacao, onAbrir }: CartaoProps) {
  return (
    <button className="card" onClick={onAbrir}>
      <div className="card-topo">
        <Retrato foto={t.foto} nome={t.nome} oculto={gravacao} />
        <div className="card-id">
          <div className="card-nome">{t.nome}</div>
          <div className="card-sub">{t.noCargo ? 'Técnico do' : 'Ex-técnico do'} {t.clube} · {t.competicao} {t.temporada}</div>
        </div>
        {t.aproveitamento != null && <div className="nota"><b>{t.aproveitamento}%</b><span>aprov.</span></div>}
      </div>
      {(t.esquemaPreferido || t.estilo) && (
        <span className="funcao">{[t.esquemaPreferido, t.estilo].filter(Boolean).join(' · ')}</span>
      )}
      <div className="fatos">
        <span>{t.jogos} jogos</span><span>{t.vitorias}V {t.empates}E {t.derrotas}D</span>
        {t.titularesAte23 != null && <span>{t.titularesAte23}% das titularidades para até 23 anos</span>}
      </div>
      {t.dimensoes.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} />)}
    </button>
  )
}

type PainelProps = {
  treinador: { id: number; nome: string }
  gravacao: boolean
  onFechar: () => void
  onAbrirAtleta: (a: ResumoAtleta) => void
}

export function PainelTreinador({ treinador, gravacao, onFechar, onAbrirAtleta }: PainelProps) {
  const [detalhe, setDetalhe] = useState<DetalheTreinador | null>(null)
  const [atletas, setAtletas] = useState<AtletaDoTreinador[] | null>(null)
  const [sugestoes, setSugestoes] = useState<SugestoesParaTreinador | null>(null)
  const [buscando, setBuscando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setDetalhe(null); setErro(null); setAtletas(null); setSugestoes(null)
    api.treinador(treinador.id).then(setDetalhe).catch((e) => setErro(e.message))
    api.atletasDoTreinador(treinador.id).then(setAtletas).catch(() => setAtletas([]))
  }, [treinador])

  function buscarSugestoes() {
    setBuscando(true)
    api.sugestoesParaTreinador(treinador.id).then(setSugestoes).catch((e) => setErro(e.message)).finally(() => setBuscando(false))
  }

  useEffect(() => {
    const fechar = (e: KeyboardEvent) => { if (e.key === 'Escape') onFechar() }
    window.addEventListener('keydown', fechar)
    return () => window.removeEventListener('keydown', fechar)
  }, [onFechar])

  const r = detalhe?.resumo
  const carreira = detalhe?.carreira

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel" role="dialog" aria-modal aria-label={treinador.nome} onClick={(e) => e.stopPropagation()}>
        <div className="perfil-topo">
          <Retrato foto={r?.foto} nome={treinador.nome} grande oculto={gravacao} />
          <div className="perfil-id">
            <h2>{treinador.nome}</h2>
            {carreira && (carreira.idade || carreira.nacionalidade) && (
              <div className="card-sub">{[carreira.idade ? `${carreira.idade} anos` : null, carreira.nacionalidade].filter(Boolean).join(' · ')}</div>
            )}
            {r && <div className="card-sub">{r.noCargo ? 'Técnico do' : 'Ex-técnico do'} {r.clube} · {r.competicao} {r.temporada}</div>}
          </div>
          {r?.aproveitamento != null && <div className="nota"><b>{r.aproveitamento}%</b><span>aprov.</span></div>}
        </div>

        {erro && <p className="erro">{erro}</p>}
        {!detalhe && !erro && <p className="pensando">Carregando perfil do treinador…</p>}

        {detalhe?.passagens.map((p) => <Passagem key={`${p.passagem.clubeId}-${p.passagem.competicaoId}-${p.passagem.temporada}`} p={p} gravacao={gravacao} />)}

        {atletas && atletas.length > 0 && (
          <section>
            <h3>Atletas que mais escalou</h3>
            <div className="cards">
              {atletas.slice(0, 6).map((x) => (
                <div key={x.atleta.id} className="com-rotulo">
                  <span className="card-sub">{x.jogosComEle} jogos como titular{x.funcaoComEle ? ` · ${x.funcaoComEle}` : ''}</span>
                  <CartaoAtleta atleta={x.atleta} gravacao={gravacao} onAbrir={() => onAbrirAtleta(x.atleta)} />
                </div>
              ))}
            </div>
          </section>
        )}

        {sugestoes && (
          <section>
            <h3>Atletas com o perfil que ele escala</h3>
            {sugestoes.funcoes.map((f) => (
              <div key={f.funcao} className="perfil-funcao">
                <div className="funcao-topo">
                  <b>{f.funcao}</b>
                  {f.percentilNaCompeticao != null && f.percentilNaCompeticao <= 35 && <span className="selo-carencia">carência no time</span>}
                  <span className="card-sub">perfil: {f.perfil.map((d) => `${d.nome.toLowerCase()} ${d.percentil}`).join(' · ')}</span>
                </div>
                <span className="card-sub">Quem ele escala: {f.quemEleEscala.join(', ')}</span>
                {f.sugestoes.length === 0
                  ? <span className="card-sub">Nenhum atleta parecido e no mesmo nível na base.</span>
                  : (
                    <div className="cards">
                      {f.sugestoes.map((s) => (
                        <CartaoAtleta key={s.atleta.id} atleta={s.atleta} semelhanca={s.semelhanca} gravacao={gravacao} onAbrir={() => onAbrirAtleta(s.atleta)} />
                      ))}
                    </div>
                  )}
              </div>
            ))}
            {sugestoes.observacoes.map((o) => <p key={o} className="legenda">{o}</p>)}
          </section>
        )}

        {carreira && carreira.passagens.length > 0 && (
          <section>
            <h3>Carreira</h3>
            <ol className="trajetoria">
              {carreira.passagens.slice(0, 12).map((c, i) => (
                <li key={i}>
                  {!gravacao && c.escudo ? <img src={c.escudo} alt="" width={22} height={22} /> : <i aria-hidden className="ponto" />}
                  <span className="t-clube">{c.clube}</span>
                  <span className="card-sub">{Anos(c.inicio, c.fim)}</span>
                </li>
              ))}
            </ol>
          </section>
        )}

        {detalhe?.observacoes.map((o) => <p key={o} className="obs">{o}</p>)}

        <div className="acoes">
          {detalhe && !sugestoes && (
            <button className="btn" disabled={buscando} onClick={buscarSugestoes}>
              {buscando ? 'Buscando…' : 'Atletas com o perfil dele'}
            </button>
          )}
          <button className="btn prim" onClick={onFechar}>Fechar</button>
        </div>
      </div>
    </div>
  )
}

function Passagem({ p, gravacao }: { p: PassagemDetalhada; gravacao: boolean }) {
  const t = p.passagem
  return (
    <section className="passagem">
      <div className="clube-linha">
        <Escudo time={{ nome: t.clube, escudo: t.escudo }} oculto={gravacao} />
        <div>
          <b>{t.clube}</b> <span className="card-sub">· {t.competicao} {t.temporada} · {Periodo(t.inicio, t.fim, t.noCargo)}</span>
        </div>
      </div>

      <div className="tiles">
        <Tile valor={`${t.vitorias}V ${t.empates}E ${t.derrotas}D`} rotulo={`em ${t.jogos} jogos`} />
        <Tile valor={t.aproveitamento != null ? `${t.aproveitamento}%` : '—'} rotulo="aproveitamento" />
        <Tile valor={t.titularesAte23 != null ? `${t.titularesAte23}%` : '—'} rotulo="das titularidades para até 23 anos" />
        <Tile valor={p.idadeMediaTitulares != null ? p.idadeMediaTitulares.toLocaleString('pt-BR') : '—'} rotulo="idade média dos titulares" />
      </div>
      {p.percentilJovens != null && (
        <Medidor nome="Espaço para jovens, em relação aos outros treinadores" valor={p.percentilJovens} mediana />
      )}

      {p.comparacao && <p className="comparacao">{p.comparacao}</p>}

      {p.estilo && <p><b>{p.estilo.rotulo}.</b> {p.estilo.explicacao}</p>}

      {t.dimensoes.length > 0 && (
        <>
          <p className="legenda">Time sob o comando dele, em percentil entre os treinadores da competição (só os jogos de cada um).</p>
          <div className="grade2">
            {t.dimensoes.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} mediana />)}
          </div>
        </>
      )}

      {p.esquemas.length > 0 && (
        <>
          <h3 style={{ marginTop: 14 }}>Esquemas</h3>
          <ListaDeEsquemas esquemas={p.esquemas} />
        </>
      )}

      {p.noMesmoClube.length > 0 && (
        <p className="card-sub">
          Outros treinadores do {t.clube} na temporada: {p.noMesmoClube.map((o) => `${o.nome} (${o.jogos} ${o.jogos === 1 ? 'jogo' : 'jogos'}${o.aproveitamento != null ? `, ${o.aproveitamento}%` : ''})`).join(' · ')}
        </p>
      )}
    </section>
  )
}

function Tile({ valor, rotulo }: { valor: string; rotulo: string }) {
  return <div className="tile"><b>{valor}</b><span>{rotulo}</span></div>
}

/** "2019–2021" ou "desde 2024". */
function Anos(inicio?: string | null, fim?: string | null) {
  const a = (s?: string | null) => (s ? s.slice(0, 4) : null)
  if (!fim) return inicio ? `desde ${a(inicio)}` : ''
  return a(inicio) === a(fim) ? `${a(fim)}` : `${a(inicio) ?? '?'}–${a(fim)}`
}
