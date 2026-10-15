/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The build lands in the API host's wwwroot, which serves it (StageZero/Api/SpaHosting.cs).
// In development, `npm run dev` serves the app on :5173 and proxies /api to `dotnet run`
// (launchSettings.json: https://localhost:5001, whose dev certificate the proxy accepts).
// docker-compose.debug.yml runs this same dev server in a container, pointed at the API
// container with STAGEZERO_API and watching by polling, since a bind mount gets no inotify.
const apiTarget = process.env.STAGEZERO_API ?? 'https://localhost:5001';
const pollWatch = process.env.STAGEZERO_WATCH_POLL === 'true';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../StageZero/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: 5173,
    watch: { usePolling: pollWatch },
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false, secure: false },
      '/healthz': { target: apiTarget, secure: false },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    css: false,
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'text', 'cobertura'],
      reportsDirectory: '../coverage/web',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/design/**', 'src/**/*.test.{ts,tsx}', 'src/test/**', 'src/main.tsx'],
    },
  },
});
