import { useEffect, useRef, useState } from 'react'
import { api, EVENTO_SESSAO_EXPIRADA, lerModoGravacao, salvarModoGravacao, type Mensagem, type ResumoAtleta, type ResumoPartida, type ResumoTime, type ResumoTreinador } from './api'
import { CartaoAtleta } from './Componentes'
import { Login } from './Login'
import { PainelAtleta } from './PainelAtleta'
import { CartaoPartida, PainelPartida } from './PainelPartida'
import { CartaoTime, PainelTime } from './PainelTime'
import { CartaoTreinador, PainelTreinador } from './PainelTreinador'

const SUGESTOES = [
  'Meu time joga no 3-4-3. Preciso de um zagueiro pela esquerda, bom no jogo aéreo e na saída de bola, até 25 anos',
  'Quais jovens até 23 anos já são titulares e estão entre os melhores da posição?',
  'Quero um volante com boa marcação e disciplina, que jogue mais da metade dos minutos',
  'Pontas esquerdas com drible e criação, até 24 anos',
  'Quais times da Série B mais precisam de um lateral esquerdo?',
  'Como joga o líder da Série A? Esquema, estilo e carências',
  'Quais treinadores da Série B mais dão espaço para jovens?',
  'Faça uma análise do último jogo do Galo',
  'Que atletas combinam com o estilo do treinador do Flamengo?',
]

/** Confere a sessão: sem login, mostra a tela de entrada. */
export default function App() {
  const [usuario, setUsuario] = useState<string | null | undefined>(undefined)

  useEffect(() => {
    api.sessao().then((s) => setUsuario(s.nome)).catch(() => setUsuario(null))
    const expirou = () => setUsuario(null)
    window.addEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
    return () => window.removeEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
  }, [])

  if (usuario === undefined) return null
  if (usuario === null) return <Login onEntrar={setUsuario} />
  return <Plataforma usuario={usuario} onSair={() => { api.logout().finally(() => setUsuario(null)) }} />
}

function Plataforma({ usuario, onSair }: { usuario: string; onSair: () => void }) {
  const [mensagens, setMensagens] = useState<Mensagem[]>([])
  const [texto, setTexto] = useState('')
  const [carregando, setCarregando] = useState(false)
  const [aberto, setAberto] = useState<ResumoAtleta | null>(null)
  const [timeAberto, setTimeAberto] = useState<ResumoTime | null>(null)
  const [treinadorAberto, setTreinadorAberto] = useState<Pick<ResumoTreinador, 'id' | 'nome'> | null>(null)
  const [partidaAberta, setPartidaAberta] = useState<ResumoPartida | null>(null)
  const [gravacao, setGravacao] = useState(lerModoGravacao)
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
      setMensagens([...historico, { papel: 'assistant', texto: r.texto, atletas: r.atletas, times: r.times, treinadores: r.treinadores, partidas: r.partidas }])
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
            AlpaFlow Scout <small>atletas, times, treinadores e jogos com IA</small>
          </div>
        </div>
        <div className="topo-acoes">
          <span className="selo">Séries A, B e C · só maiores de 18 anos</span>
          {mensagens.length > 0 && (
            <button className="btn" onClick={() => { setMensagens([]); setAberto(null); setTimeAberto(null); setTreinadorAberto(null); setPartidaAberta(null); setTexto('') }} disabled={carregando}>
              Limpar conversa
            </button>
          )}
          <label className="alternar" title="Esconde fotos e escudos para gravar vídeos públicos">
            <input type="checkbox" checked={gravacao} onChange={(e) => { setGravacao(e.target.checked); salvarModoGravacao(e.target.checked) }} />
            Modo gravação
          </label>
          <button className="btn" onClick={onSair} title={`Conectado como ${usuario}`}>Sair</button>
        </div>
      </header>

      <main className="conversa" aria-live="polite">
        {mensagens.length === 0 && (
          <section className="vazio">
            <h1>Que atleta o seu time precisa?</h1>
            <p>Descreva o perfil com as suas palavras: posição, lado, esquema, idade e características. Ou pergunte sobre um time, um treinador ou um jogo. A IA busca na base e explica cada escolha.</p>
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
            {m.partidas && m.partidas.length > 0 && (
              <div className="cards">
                {m.partidas.map((p) => <CartaoPartida key={p.id} partida={p} gravacao={gravacao} onAbrir={() => setPartidaAberta(p)} />)}
              </div>
            )}
            {m.times && m.times.length > 0 && (
              <div className="cards">
                {m.times.map((t) => <CartaoTime key={`${t.id}-${t.competicaoId}`} time={t} gravacao={gravacao} onAbrir={() => setTimeAberto(t)} />)}
              </div>
            )}
            {m.treinadores && m.treinadores.length > 0 && (
              <div className="cards">
                {m.treinadores.map((t) => <CartaoTreinador key={`${t.id}-${t.clubeId}`} treinador={t} gravacao={gravacao} onAbrir={() => setTreinadorAberto(t)} />)}
              </div>
            )}
            {m.atletas && m.atletas.length > 0 && (
              <div className="cards">
                {m.atletas.map((a) => <CartaoAtleta key={a.id} atleta={a} gravacao={gravacao} onAbrir={() => setAberto(a)} />)}
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

      {aberto && <PainelAtleta atleta={aberto} gravacao={gravacao} onFechar={() => setAberto(null)} onAbrir={setAberto} onAbrirTime={(t) => { setAberto(null); setTimeAberto(t) }} />}
      {timeAberto && (
        <PainelTime time={timeAberto} gravacao={gravacao} onFechar={() => setTimeAberto(null)} onAbrirAtleta={(a) => { setTimeAberto(null); setAberto(a) }}
          onAbrirTreinador={(t) => { setTimeAberto(null); setTreinadorAberto(t) }} />
      )}
      {treinadorAberto && <PainelTreinador treinador={treinadorAberto} gravacao={gravacao} onFechar={() => setTreinadorAberto(null)}
        onAbrirAtleta={(a) => { setTreinadorAberto(null); setAberto(a) }} />}
      {partidaAberta && <PainelPartida partida={partidaAberta} gravacao={gravacao} onFechar={() => setPartidaAberta(null)} />}
    </div>
  )
}
