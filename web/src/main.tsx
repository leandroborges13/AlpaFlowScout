import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { PaginaJogo, idDaPaginaPublica } from './PaginaJogo'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/* /jogo/<id> é a página pública do jogo (sem login); o resto é o Scout */}
    {idDaPaginaPublica(location.pathname) == null ? <App /> : <PaginaJogo id={idDaPaginaPublica(location.pathname)!} />}
  </StrictMode>,
)

// App instalável: o service worker só entra na versão publicada (no desenvolvimento atrapalharia o recarregamento)
if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => { navigator.serviceWorker.register('/sw.js').catch(() => {}) })
}
