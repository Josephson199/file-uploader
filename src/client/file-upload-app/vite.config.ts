import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

const configPlugin = {
  name: 'config-endpoint',
  configureServer(server: any) {
    server.middlewares.use('/config', (_req: any, res: any) => {
      const maxFileSize = parseInt(process.env.UPLOAD_MAX_FILE_SIZE || '104857600');
      const allowedExtensions = process.env.UPLOAD_ALLOWED_EXTENSIONS
        ? process.env.UPLOAD_ALLOWED_EXTENSIONS.split(',').map((e: string) => e.trim())
        : [];
      const allowedMimeTypes = process.env.UPLOAD_ALLOWED_MIME_TYPES
        ? process.env.UPLOAD_ALLOWED_MIME_TYPES.split(',').map((m: string) => m.trim())
        : [];
      const maxFileNameLength = parseInt(process.env.UPLOAD_MAX_FILE_NAME_LENGTH || '255');

      const config = {
        keycloak: {
          baseUrl: process.env.KEYCLOAK_BASE_URL,
          realm: process.env.KEYCLOAK_REALM,
        },
        upload: {
          maxFileSize,
          allowedExtensions,
          allowedMimeTypes,
          maxFileNameLength,
        }
      };

      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify(config));
    });
  }
};

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  return {
    resolve: {
      alias: {
        '@uppy': path.resolve(__dirname, 'node_modules/@uppy'),
      },
    },

    plugins: [react(), configPlugin],

    server: {
      port: parseInt(env.VITE_PORT),
      // NO PROXY HERE — ASP.NET handles all proxying
    },

    build: {
      outDir: 'dist',
      rollupOptions: {
        input: './index.html'
      }
    }
  }
})
