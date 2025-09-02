// import { defineConfig } from 'vite'
// import react from '@vitejs/plugin-react'

// // https://vite.dev/config/
// export default defineConfig({
//   plugins: [react()],
// })
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  return {
    resolve:{
    alias: {
      '@uppy': path.resolve(__dirname, 'node_modules/@uppy'),
    },
  },
    plugins: [react()],
    server:{
      port: parseInt(env.VITE_PORT),
      proxy: {
        // "apiservice" is the name of the API in AppHost.cs.
        '/api': {
          target: process.env.services__apiservice__https__0 || process.env.services__apiservice__http__0,
          changeOrigin: true,
          secure: false,
          rewrite: (path) => path.replace(/^\/api/, '')
        },
        '/files': {
          target: process.env.services__apiservice__https__0 || process.env.services__apiservice__http__0,
          changeOrigin: true,
          secure: false
      },
      }
    },
    build:{
      outDir: 'dist',
      rollupOptions: {
        input: './index.html'
      }
    }
  }
})