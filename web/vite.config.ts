import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// O front chama /api e o Vite repassa para a API .NET (SCOUT_API_URL troca o destino, ex.: para testes)
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': process.env.SCOUT_API_URL ?? 'http://localhost:5232' } },
})
