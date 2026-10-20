import { useState, type FormEvent } from 'react'
import { api } from './api'

/** WhatsApp da equipe comercial, com a mensagem já escrita. */
const CONTATO_COMERCIAL = `https://wa.me/5531998725328?text=${encodeURIComponent('Olá! Vi a AlpaFlow Scout e quero saber como usar a plataforma.')}`

export function Login({ onEntrar }: { onEntrar: (nome: string) => void }) {
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  async function entrar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const r = await api.login(email, senha)
      onEntrar(r.nome)
    } catch (e) {
      setErro((e as Error).message)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <main className="login">
      <form className="login-caixa" onSubmit={entrar}>
        <div className="marca"><i aria-hidden>⚽</i><div>AlpaFlow Scout</div></div>
        <p className="card-sub">Análise de atletas com IA. Acesso restrito.</p>
        <label>
          E-mail
          <input type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Senha
          <input type="password" autoComplete="current-password" value={senha} onChange={(e) => setSenha(e.target.value)} required />
        </label>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="btn prim" type="submit" disabled={enviando}>{enviando ? 'Entrando…' : 'Entrar'}</button>
        <div className="contato-comercial">
          <p><b>Quer usar a plataforma no seu clube ou empresa?</b><br />Fale com a nossa equipe comercial.</p>
          <a className="btn whatsapp" href={CONTATO_COMERCIAL} target="_blank" rel="noopener noreferrer">💬 Falar no WhatsApp</a>
        </div>
      </form>
    </main>
  )
}
