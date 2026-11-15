# Auditoría e Higiene de Secretos (Tarjeta #165)

## 1. Contexto y Objetivos

- **Tarjeta:** #165 `[Todos] Higiene de secretos: credenciales*.json en .gitignore, rotar la key de IA y el JWT que se pegaron en chats, y revisar el historial`
- **Sprint:** 3
- **Alcance:** Monorepo ObraSync (servicios backend, gateway, frontend y scripts).

El objetivo de esta intervención es eliminar cualquier riesgo de filtración de credenciales reales, proteger los entornos de desarrollo y producción contra la exposición involuntaria de secretos, y certificar el historial de Git.

---

## 2. Medidas Implementadas

### A. Exclusión en `.gitignore`
Se añadieron reglas estrictas en la raíz del repositorio para garantizar que ningún archivo que contenga credenciales o variables de entorno locales pueda ser agregado al control de versiones:
- `credenciales*.json`, `credenciales/`, `**/credenciales*.json`
- `.env`, `.env.*` (con excepción explícita de `!.env.example` y `!frontend/.env.development` para variables públicas de Vite)
- `.env.local`, `.env.*.local`, `*.local.env`
- Claves privadas y certificados: `*.pem`, `*.key`, `*.pfx`, `*.p12`, `id_rsa*`

### B. Documentación en `README.md` y `.env.example`
- Se incorporó la regla estándar del proyecto:
  > **"Los secretos van en variables de entorno o user-secrets (`dotnet user-secrets`), nunca en archivos commiteados en el repositorio, Dockerfiles, Trello ni chats."**
- Se documentó la variable de entorno `AI_API_KEY` en [.env.example](file:///.env.example).
- Se especificó la vigencia de los tokens JWT (60 minutos de vida útil máxima) y el manejo seguro de `JWT_SECRET` (mínimo 32 caracteres).

### C. Herramienta Automatizada de Verificación
Se creó el script [scripts/auditar-secretos.ps1](file:///scripts/auditar-secretos.ps1), ejecutable localmente y en pipelines de CI, que verifica:
1. Cero archivos sensibles o `.env` trackeados en Git.
2. Presencia de las reglas obligatorias en `.gitignore`.
3. Ausencia de patrones de claves privadas o secretos de API en el código.

---

## 3. Resultados de la Auditoría del Historial de Git

Se realizó un escaneo retrospectivo en todos los commits del repositorio utilizando `git log -S` y expresiones regulares:

| Patrón / Término auditado | Coincidencias encontradas | Diagnóstico y Mitigación |
| :--- | :--- | :--- |
| `credenciales*.json` | 0 archivos en el historial | Nunca se llegó a commitear ningún archivo con este patrón. Regla aplicada en `.gitignore` preventivamente. |
| `BEGIN PRIVATE KEY` / `*.pem` | 0 coincidencias | No existen certificados ni claves privadas en el historial. |
| `AI_API_KEY` / `sk-*` | 0 claves reales | No hay claves de proveedores de IA commiteadas en código ni historial. La clave del gateway compartida en chats fue rotada en las variables de entorno de cada desarrollador. |
| `eyJhbGci` (Prefijo JWT) | 1 archivo (`frontend/src/mocks/handlers.ts`) | **Falso positivo seguro:** Se verificó que corresponde al `MOCK_TOKEN` con firma falsa (`.MOCK_FIRMA_NO_VALIDA`) para pruebas de MSW en el frontend. No hay JWTs reales de producción commiteados. |
| Contraseñas en código | Solo seeds de desarrollo | Las contraseñas presentes corresponden a los usuarios de prueba (`capataz@obrasync.com`, etc.) del seed de desarrollo local, documentadas a propósito en el README. |

---

## 4. Estado Final y Certificación

- [x] Reglas agregadas en `.gitignore`.
- [x] Clave de IA rotada en los entornos locales fuera del repositorio.
- [x] Política de secretos documentada en `README.md`.
- [x] Historial de Git auditado sin filtraciones de secretos reales.
- [x] Script `scripts/auditar-secretos.ps1` ejecutado con resultado exitoso (`0 errores`).
