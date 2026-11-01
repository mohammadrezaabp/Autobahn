import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';

export default defineConfig({
  plugins: [solid()],
  base: '/admin/',
  resolve: { dedupe: ['solid-js', '@solidjs/router'] },
  server: { fs: { allow: ['.', '../../design', '../Autobahn.Fleet.Web/src'] } },
  build: {
    outDir: '../Autobahn.Host/wwwroot/admin',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('/pages/ratelimits/')) return 'ratelimits';
          // Named, so the rate-limit chunk does not swallow what its table reads names from.
          if (/\/stores\/(keys|tenants|modelOptions)\.ts$/.test(id)) return 'access';
        },
      },
    },
  },
});
