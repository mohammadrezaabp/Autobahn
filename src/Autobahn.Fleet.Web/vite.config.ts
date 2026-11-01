/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';

export default defineConfig({
  plugins: [solid()],
  // Served by the Autobahn host alongside the gateway workspace.
  base: '/admin/fleet/',
  build: {
    outDir: '../Autobahn.Host/wwwroot/admin/fleet',
    emptyOutDir: true,
  },
  server: {
    fs: { allow: ['.', '../../design'] },
    proxy: { '/admin/fleet/api': 'http://127.0.0.1:8080' },
  },
  test: {
    globals: true,
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
