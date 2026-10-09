// Service worker do Scout: guarda só a "casca" do app (HTML, JS, CSS, ícones) para abrir rápido.
// Nada de /api: os dados de atletas e o chat sempre vêm do servidor, com login.
const VERSAO = 'scout-v1'
const CASCA = ['/', '/manifest.webmanifest', '/favicon.svg', '/icone-192.png', '/icone-512.png']

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(VERSAO).then((c) => c.addAll(CASCA)).then(() => self.skipWaiting()))
})

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys()
      .then((chaves) => Promise.all(chaves.filter((k) => k !== VERSAO).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url)
  if (e.request.method !== 'GET' || url.origin !== location.origin || url.pathname.startsWith('/api')) return

  // Páginas: rede primeiro (pega a versão nova), cache se estiver sem internet
  if (e.request.mode === 'navigate') {
    e.respondWith(fetch(e.request).catch(() => caches.match('/')))
    return
  }
  // Arquivos estáticos: cache primeiro, e atualiza em segundo plano
  e.respondWith(
    caches.match(e.request).then((guardado) => {
      const rede = fetch(e.request).then((r) => {
        if (r.ok) caches.open(VERSAO).then((c) => c.put(e.request, r.clone()))
        return r
      })
      return guardado ?? rede
    }),
  )
})
