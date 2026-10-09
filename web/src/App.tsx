import { useEffect, useRef, useState } from 'react'
import { api, lerModoGravacao, salvarModoGravacao, type Mensagem, type ResumoAtleta } from './api'
import { CartaoAtleta } from './Componentes'
import { PainelAtleta } from './PainelAtleta'

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
        <div className="topo-acoes">
          <span className="selo">Séries A, B e C · só maiores de 18 anos</span>
          <label className="alternar" title="Esconde fotos e escudos para gravar vídeos públicos">
            <input type="checkbox" checked={gravacao} onChange={(e) => { setGravacao(e.target.checked); salvarModoGravacao(e.target.checked) }} />
            Modo gravação
          </label>
        </div>
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

      {aberto && <PainelAtleta atleta={aberto} gravacao={gravacao} onFechar={() => setAberto(null)} onAbrir={setAberto} />}
    </div>
  )
}
