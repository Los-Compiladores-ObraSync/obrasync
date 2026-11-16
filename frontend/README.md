## Datos reales o simulados

- Por defecto el frontend usa el backend real (`VITE_USE_MOCKS=false` en `.env.development`).
- Para trabajar sin backend, creá `frontend/.env.development.local` con la línea `VITE_USE_MOCKS=true`.
  Ese archivo no se sube a git y manda por encima de `.env.development`.
- En modo simulado se ve una franja "MODO SIMULADO" arriba de todo.
- Usuarios simulados: `capataz@obrasync.com`, `comprador@obrasync.com`, `proveedor@obrasync.com` y `admin@obrasync.com`, con la contraseña `Password123!` (la misma que el seed del backend).

# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some ESLint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the ESLint configuration

If you are developing a production application, we recommend updating the configuration to enable type-aware lint rules:

```js
export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      // Other configs...

      // Remove tseslint.configs.recommended and replace with this
      tseslint.configs.recommendedTypeChecked,
      // Alternatively, use this for stricter rules
      tseslint.configs.strictTypeChecked,
      // Optionally, add this for stylistic rules
      tseslint.configs.stylisticTypeChecked,

      // Other configs...
    ],
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.node.json', './tsconfig.app.json'],
        tsconfigRootDir: import.meta.dirname,
      },
      // other options...
    },
  },
])

```

You can also install [eslint-plugin-react-x](https://npmx.dev/package/eslint-plugin-react-x) and [eslint-plugin-react-dom](https://npmx.dev/package/eslint-plugin-react-dom) for React-specific lint rules:

```js
// eslint.config.js
import reactX from 'eslint-plugin-react-x'
import reactDom from 'eslint-plugin-react-dom'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      // Other configs...
      // Enable lint rules for React
      reactX.configs['recommended-typescript'],
      // Enable lint rules for React DOM
      reactDom.configs.recommended,
    ],
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.node.json', './tsconfig.app.json'],
        tsconfigRootDir: import.meta.dirname,
      },
      // other options...
    },
  },
])

```

## Decisión: dónde se guarda el token JWT (#136)

**Propuesta: se mantiene en `localStorage`** (clave `obrasync_session`), con estas razones y límites:

- Es una PWA mobile-first: la sesión tiene que sobrevivir a recargas y al cierre de la app. `sessionStorage` la perdería.
- Pasar a cookie `HttpOnly` obliga a cambiar Usuarios (emitir la cookie), el Gateway (CORS con credenciales) y agregar protección CSRF. Es una decisión de arquitectura que hay que acordar entre squads, y no entra en el plazo.
- El riesgo real de `localStorage` es XSS (un script inyectado puede leer el token). Se mitiga así: el token vence en poco tiempo (referencia de 60 minutos, fijado en el contrato), el código no usa `dangerouslySetInnerHTML` ni `innerHTML`, y React escapa el contenido por defecto.
- Si se revisa a futuro, el cambio queda concentrado en `src/auth/` (`sessionContext.ts` y `AuthContext.tsx`).

Falta confirmar esta decisión con el equipo.

## Correr en Docker

El frontend también se levanta con Docker, junto con el resto del sistema. Desde la **raíz del repositorio** (no desde `frontend`):

```powershell
docker compose up --build
```

Después abrí `http://localhost:5173`.

Cómo funciona:

- El `Dockerfile` compila la app con Node y la sirve con nginx (imagen `nginx-unprivileged`, escucha en el 8080 dentro del contenedor).
- nginx redirige las llamadas a `/api/` al API Gateway (`gateway:8080`), igual que el proxy de Vite en desarrollo, así que el navegador siempre habla con el mismo origen.
- nginx no cachea `index.html` ni `sw.js` (para que la PWA se actualice) y sí cachea los archivos de `/assets/`.
- nginx manda cabeceras de seguridad (#199): `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` y una `Content-Security-Policy` que solo deja cargar lo del mismo origen (más `nominatim.openstreetmap.org` para comprobar direcciones de obras). Están en `nginx-cabeceras-seguridad.conf` y se incluyen en cada `location` que tiene su propio `add_header` (si no, nginx las pierde). Para comprobarlas con el sistema levantado: `curl -sI http://localhost:5173/`, `curl -sI http://localhost:5173/sw.js` y `curl -sI http://localhost:5173/assets/<archivo>.js` tienen que mostrar las cuatro. Después abrí la app en el navegador, mirá que la consola no muestre errores de "Content Security Policy" y que el service worker siga registrado.
- Tiempo límite de los pedidos (#199): `apiFetch` corta a los 15 s (60 s el asistente) y muestra el error "No pudimos conectarnos, reintentá." (`ApiError` con `status` 0).
- Hace falta un archivo `.env` en la raíz, copiado de `.env.example`, con tus propios valores. El `.env` no se sube al repositorio.

Pendiente de acuerdo del equipo: el uso de nginx y el puerto 5173 son provisionales (el puerto del frontend todavía no está definido).

Para desarrollar con recarga en caliente sigue valiendo `npm run dev` (ver "Datos reales o simulados").
