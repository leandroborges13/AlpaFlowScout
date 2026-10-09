import { useEffect, useRef, useState } from 'react'
import { api, type DetalheAtleta, type Mensagem, type Parecidos, type ResumoAtleta } from './api'

const SUGESTOES = [
  'Meu time joga no 3-4-3. Preciso de um zagueiro pela esquerda, bom no jogo aéreo e na saída de bola, até 25 anos',
  'Quais jovens até 23 anos já são titulares e estão entre os melhores da posição?',
  'Quero um volante com boa marcação e disciplina, que jogue mais da metade dos minutos',
  'Pontas esquerdas com drible e criação, até 24 anos',
]

export default function App() {
  const [mensagens, setMensagens] = useState<Mensagem[]>([])
  const [texto, setTexto] = useState('')
  const [carregando, setCarregando] = useState(false)
  const [aberto, setAberto] = useState<ResumoAtleta | null>(null)
  const fim = useRef<HTMLDivElement>(null)

  useEffect(() => {
    fim.current?.scrollIntoView({ behavior: 'smooth' })
  }, [mensagens, carregando])

  async function enviar(conteudo: string) {
    const pergunta = conteudo.trim()
    if (!pergunta || carregando) return
    const historico: Mensagem[] = [...mensagens, { papel: 'user', texto: pergunta }]
    setMensagens(historico)
    setTexto('')
    setCarregando(true)
    try {
      const r = await api.chat(historico)
      setMensagens([...historico, { papel: 'assistant', texto: r.texto, atletas: r.atletas }])
    } catch (e) {
      setMensagens([...historico, { papel: 'assistant', texto: `Não consegui responder agora: ${(e as Error).message}` }])
    } finally {
      setCarregando(false)
    }
  }

  return (
    <div className="app">
      <header className="topo">
        <div className="marca">
          <i aria-hidden>⚽</i>
          <div>
            AlpaFlow Scout <small>análise de atletas com IA</small>
          </div>
        </div>
        <span className="selo">Séries A, B e C · só maiores de 18 anos</span>
      </header>

      <main className="conversa" aria-live="polite">
        {mensagens.length === 0 && (
          <section className="vazio">
            <h1>Que atleta o seu time precisa?</h1>
            <p>Descreva o perfil com as suas palavras: posição, lado, esquema, idade e características. A IA busca na base e explica cada escolha.</p>
            <div className="sugestoes">
              {SUGESTOES.map((s) => (
                <button key={s} onClick={() => enviar(s)}>{s}</button>
              ))}
            </div>
          </section>
        )}

        {mensagens.map((m, i) => (
          <div key={i} className={`msg ${m.papel}`}>
            <div className="balao">{m.texto}</div>
            {m.atletas && m.atletas.length > 0 && (
              <div className="cards">
                {m.atletas.map((a) => <CartaoAtleta key={a.id} atleta={a} onAbrir={() => setAberto(a)} />)}
              </div>
            )}
          </div>
        ))}
        {carregando && <div className="msg assistant"><div className="balao pensando">Analisando a base…</div></div>}
        <div ref={fim} />
      </main>

      <footer className="entrada">
        <form className="caixa" onSubmit={(e) => { e.preventDefault(); enviar(texto) }}>
          <textarea
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); enviar(texto) } }}
            placeholder="Ex.: lateral direito ofensivo, até 23 anos, que já seja titular"
            rows={1}
            aria-label="Descreva o atleta que você procura"
          />
          <button type="submit" disabled={carregando || !texto.trim()}>Buscar</button>
        </form>
        <p className="rodape">Notas e percentis calculados pela AlpaFlow Scout. Uso interno.</p>
      </footer>

      {aberto && <PainelAtleta atleta={aberto} onFechar={() => setAberto(null)} onAbrir={setAberto} />}
    </div>
  )
}

function Medidor({ nome, valor }: { nome: string; valor: number }) {
  return (
    <div className="medidor" title={`Melhor que ${Math.round(valor)}% dos atletas do mesmo setor`}>
      <span>{nome}</span>
      <b>{Math.round(valor)}</b>
      <div className="trilho"><i style={{ width: `${Math.max(2, valor)}%` }} /></div>
    </div>
  )
}

function CartaoAtleta({ atleta: a, onAbrir, semelhanca }: { atleta: ResumoAtleta; onAbrir: () => void; semelhanca?: number }) {
  const nota = semelhanca ?? a.notaEncaixe ?? a.nota
  const rotulo = semelhanca != null ? 'semelhança' : a.notaEncaixe != null ? 'encaixe' : 'nota'
  return (
    <button className="card" onClick={onAbrir}>
      <div className="card-topo">
        <div>
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
        <span>{a.participacaoMinutos}% dos minutos</span>{a.gols > 0 && <span>{a.gols} gols</span>}
      </div>
      {a.destaques.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} />)}
    </button>
  )
}

function PainelAtleta({ atleta, onFechar, onAbrir }: { atleta: ResumoAtleta; onFechar: () => void; onAbrir: (a: ResumoAtleta) => void }) {
  const [detalhe, setDetalhe] = useState<DetalheAtleta | null>(null)
  const [parecidos, setParecidos] = useState<Parecidos | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setDetalhe(null); setParecidos(null); setErro(null)
    api.atleta(atleta.id, atleta.temporada).then(setDetalhe).catch((e) => setErro(e.message))
  }, [atleta])

  useEffect(() => {
    const fechar = (e: KeyboardEvent) => e.key === 'Escape' && onFechar()
    window.addEventListener('keydown', fechar)
    return () => window.removeEventListener('keydown', fechar)
  }, [onFechar])

  const a = detalhe?.resumo ?? atleta
  const observacoes = [...new Set((detalhe?.atributos ?? []).map((x) => x.observacao).filter(Boolean))]

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel" role="dialog" aria-modal aria-label={a.nome} onClick={(e) => e.stopPropagation()}>
        <div className="card-topo">
          <div>
            <h2>{a.nome}</h2>
            <div className="card-sub">{a.idade} anos · {a.clube} · {a.competicao} {a.temporada}{a.alturaCm ? ` · ${a.alturaCm} cm` : ''}</div>
          </div>
          {a.nota != null && <div className="nota"><b>{Math.round(a.nota)}</b><span>nota geral</span></div>}
        </div>

        <div className="fatos">
          <span>{a.jogos} jogos ({a.titular} como titular)</span><span>{a.minutos.toLocaleString('pt-BR')} min</span>
          <span>{a.participacaoMinutos}% dos minutos do clube</span><span>{a.gols} gols</span><span>{a.assistencias} assistências</span>
        </div>

        {erro && <p className="erro">{erro}</p>}
        {!detalhe && !erro && <p className="pensando">Carregando perfil…</p>}

        {detalhe && (
          <>
            {Object.keys(detalhe.funcoes).length > 0 && (
              <section>
                <h3>Funções em campo (jogos como titular)</h3>
                <div className="chips">
                  {Object.entries(detalhe.funcoes).sort((x, y) => y[1] - x[1]).map(([f, n]) => <span key={f}>{f}: {n}</span>)}
                </div>
                <div className="chips" style={{ marginTop: 6 }}>
                  {Object.entries(detalhe.esquemas).sort((x, y) => y[1] - x[1]).map(([f, n]) => <span key={f}>{f}: {n}</span>)}
                </div>
              </section>
            )}
            <section>
              <h3>Atributos (percentil no setor)</h3>
              <div className="grade2">{detalhe.atributos.map((d) => <Medidor key={d.nome} nome={d.nome} valor={d.percentil} />)}</div>
              {observacoes.map((o) => <p key={o} className="obs">{o}</p>)}
            </section>
          </>
        )}

        {parecidos && (
          <section>
            <h3>Perfis parecidos</h3>
            <div className="cards">
              {parecidos.parecidos.map((p) => (
                <CartaoAtleta key={p.atleta.id} atleta={p.atleta} semelhanca={p.semelhanca} onAbrir={() => onAbrir(p.atleta)} />
              ))}
            </div>
          </section>
        )}

        <div className="acoes">
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
