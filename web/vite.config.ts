import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// O front chama /api e o Vite repassa para a API .NET
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': 'http://localhost:5232' } },
})
