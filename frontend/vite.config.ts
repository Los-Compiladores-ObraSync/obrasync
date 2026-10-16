import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
    plugins: [
        react(),
        VitePWA({
            // El service worker se actualiza solo cuando se publica una versión nueva.
            registerType: 'autoUpdate',
            includeAssets: ['favicon.ico', 'favicon.svg', 'apple-touch-icon-180x180.png'],
            manifest: {
                name: 'ObraSync',
                short_name: 'ObraSync',
                description:
                    'Plataforma de requisición, licitación y abastecimiento para obras de construcción',
                lang: 'es',
                start_url: '/',
                display: 'standalone', // se abre como app, sin la barra del navegador
                orientation: 'portrait',
                // Colores provisorios: faltan definir los de la marca.
                theme_color: '#ffffff',
                background_color: '#ffffff',
                icons: [
                    { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
                    { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
                    { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
                    {
                        src: 'maskable-icon-512x512.png',
                        sizes: '512x512',
                        type: 'image/png',
                        purpose: 'maskable',
                    },
                ],
            },
        }),
    ],
})