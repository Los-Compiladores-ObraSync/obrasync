# ObraSync

Plataforma B2B de requisición, licitación y abastecimiento para obras.

## Cómo levantar todo con Docker (backend + Gateway + frontend)

Requisitos: Docker Desktop abierto y funcionando.

1. **Crear el archivo `.env`** en la raíz del repositorio, a partir del ejemplo:

   ```
   copy .env.example .env
   ```

   Abrirlo y completar `SQL_SA_PASSWORD`, `JWT_SECRET`, `RABBITMQ_USER` y `RABBITMQ_PASS`. Las demás variables (presupuesto semilla, dirección del frontend para los mails, claves de IA y de push) vienen comentadas en `.env.example`. El `JWT_SECRET` tiene que ser **el mismo para todo el equipo** (pedirlo por el grupo del equipo). Por seguridad (#169), ya no se usan credenciales por defecto (`guest/guest`) y los servicios exigen credenciales explícitas al usar RabbitMQ. El archivo `.env` no se sube al repositorio.

2. **Levantar:**

   ```
   docker compose up --build
   ```

   La primera vez tarda varios minutos (descarga imágenes y compila los 4 servicios, el Asistente de IA, el Gateway y el frontend). SQL Server y RabbitMQ tienen que estar "healthy" antes de que arranquen los servicios. También levanta Mailpit, el correo de prueba (ver más abajo).

3. **Bajar todo:**

   ```
   docker compose down
   ```

   Para borrar también los datos de las bases (empezar de cero): `docker compose down -v`.

> Si cambiás `SQL_SA_PASSWORD` después de haber levantado SQL Server una vez, hay que correr `docker compose down -v`: el volumen guarda la contraseña anterior.

## Puertos y Red (#169)

Por seguridad, todos los puertos expuestos al host están explícitamente restringidos a `127.0.0.1` (localhost) en `docker-compose.yml`, evitando la publicación no deseada hacia la red local (LAN) o interfaces externas.

| Qué | Puerto en tu computadora (localhost) |
| --- | --- |
| Frontend (provisional, pendiente de acuerdo del equipo) | 127.0.0.1:5173 |
| API Gateway (la puerta de entrada) | 127.0.0.1:5000 |
| Usuarios y Obras | 127.0.0.1:5001 |
| Requisiciones y Catálogo | 127.0.0.1:5002 |
| Licitaciones y Cotizaciones | 127.0.0.1:5003 |
| Recepción y Presupuesto | 127.0.0.1:5004 |
| Asistente de IA (y servidor MCP) | 127.0.0.1:5005 |
| Mailpit (bandeja del correo de prueba) | 127.0.0.1:8025 |
| SQL Server | 127.0.0.1:1433 |
| RabbitMQ (servicios AMQP) | 127.0.0.1:5672 |
| RabbitMQ (panel web, credenciales `RABBITMQ_USER` / `RABBITMQ_PASS`) | 127.0.0.1:15672 |

Dentro de Docker cada servicio escucha en el 8080 (corriendo bajo usuario no-root `USER $APP_UID`); los puertos de la tabla son los mapeados a `127.0.0.1`.

**Requisiciones y la validación de obras (#178):** el servicio de Requisiciones consulta a Usuarios y Obras para comprobar que el Capataz tenga asignada la obra. Necesita `UsuariosObras__BaseUrl`: en Docker ya viene en el compose y en Visual Studio lo trae `launchSettings.json` (`http://localhost:5001`). Si falta, el servicio no arranca. Si Usuarios y Obras no responde, crear una requisición como Capataz devuelve 503 (no 403).

**Eventos que fallan (#179):** si un consumidor de eventos (Licitaciones: `RequisicionAprobadaEvent`; Recepción: `OrdenCompraEmitidaEvent`) tiene un error pasajero, MassTransit reintenta hasta 5 veces con espera creciente (de 1 a 30 segundos). Si se agotan los reintentos, el mensaje queda en la cola `<nombre>_error`: se ve en el panel de RabbitMQ (`http://localhost:15672`, pestaña Queues, cola terminada en `_error`, botón Get messages). Los eventos inválidos no se reintentan: se descartan.

## Correo de prueba: recuperar contraseña (Mailpit)

Al pedir "Olvidé mi contraseña", el servicio de Usuarios manda un mail con el enlace para elegir una contraseña nueva. En desarrollo ese mail lo recibe **Mailpit**, un servidor de correo de prueba que corre en el `docker compose` y **no manda nada a internet**.

1. En el login, tocá "Olvidé mi contraseña" y poné el correo de un usuario que exista (por ejemplo `capataz@obrasync.com`).
2. Abrí la bandeja: `http://localhost:8025`. Ahí está el mail con el enlace; tocalo y elegí la contraseña nueva.

- La pantalla responde siempre lo mismo, exista o no el correo (para no revelar qué cuentas existen). Si el correo no existe, no llega nada a Mailpit.
- El enlace **también sale por el log** de Usuarios (línea `[PASSWORD-RESET]`): `docker compose logs usuarios-obras-api`. Es el respaldo si el mail falla.
- Si falta `Correo__Host` (por ejemplo al correr Usuarios sin Docker), no se manda ningún mail y queda solo el log.
- El enlace del mail apunta a `FRONTEND_BASE_URL` (por defecto `http://localhost:5173`).
- Mailpit es solo para desarrollo y demo: para mandar mails de verdad hay que elegir un servidor SMTP real, y eso es una decisión del equipo.

## Probar desde el celular (túnel HTTPS)

Los puertos están cerrados a `127.0.0.1`, así que el celular no ve tu computadora. Para probar la PWA en el celular (instalarla, avisos, cámara para las fotos del remito) hace falta una dirección **HTTPS**. El `docker compose` trae un túnel opcional de Cloudflare ("quick tunnel": sin cuenta ni configuración).

### Paso a paso

1. En la computadora, levantá todo con el perfil `movil` (necesita internet):

   ```powershell
   docker compose --profile movil up -d --build
   ```

2. Pedí la dirección del túnel (espera a que esté lista y la muestra):

   ```powershell
   .\scripts\direccion-tunel.ps1
   ```

   Sin el script: `docker compose logs tunel` y buscá una línea con `https://<palabras>.trycloudflare.com`.

3. Abrí esa dirección en el navegador del celular (Chrome en Android, Safari en iPhone). El celular puede estar con datos móviles o con otro Wi-Fi: no hace falta que esté en tu red. El frontend ya manda `/api` al Gateway por la misma dirección, así que no hace falta otro túnel.

4. Iniciá sesión con un usuario del sistema (Capataz, Comprador o Proveedor): cada rol entra a su portal.

5. (Opcional) Instalá la app para usarla como una aplicación:
   - **Android (Chrome):** menú ⋮ → "Instalar aplicación" o "Agregar a la pantalla principal".
   - **iPhone (Safari):** botón Compartir → "Agregar a pantalla de inicio".

6. Cuando el navegador pida permiso para la **cámara** (fotos del remito y de los problemas de entrega), tocá "Permitir". Solo funciona por HTTPS, por eso se usa el túnel.

7. Al terminar, apagá el túnel:

   ```powershell
   docker compose --profile movil down
   ```

### Cosas a tener en cuenta

- **La dirección cambia cada vez** que arranca el túnel.
- **Cualquiera que tenga la dirección puede abrir el login de tu app.** Usalo solo para probar y apagalo después: `docker compose --profile movil down` (o `docker compose stop tunel`).
- Para que el enlace del mail de recuperación abra en el celular, poné en `.env` `FRONTEND_BASE_URL=https://<la-direccion>.trycloudflare.com` y reiniciá Usuarios (`docker compose up -d usuarios-obras-api`).
- Los Magic Links usan la dirección desde la que abrís la app: si los generás desde el celular o desde la dirección del túnel, el proveedor los abre bien afuera de tu red. Si los generás desde `localhost`, solo funcionan en tu computadora.
- El túnel necesita internet y no tiene garantía de disponibilidad: es una ayuda para probar, no para producción. Es una herramienta nueva que todavía no se acordó con el equipo.

## Probar que está andando

- Frontend: `http://localhost:5173` debe mostrar el login.
- Gateway: `http://localhost:5000/api/usuarios/tokens` (POST) debe llegar al servicio de Usuarios.
- Salud de los servicios: `http://localhost:5002/health` (Requisiciones), `http://localhost:5003/health` (Licitaciones) y `http://localhost:5004/health` (Recepción).
- Swagger (se activa en `Development`): `http://localhost:5001/swagger` (Usuarios), `http://localhost:5002/swagger` (Requisiciones) y `http://localhost:5004/swagger` (Recepción).
- RabbitMQ: `http://localhost:15672`.
- Correo de prueba (Mailpit): `http://localhost:8025`.

## Frontend

Con `docker compose up --build` el frontend ya queda funcionando en `http://localhost:5173` y habla con el Gateway (ver `frontend/README.md`, sección "Correr en Docker").

Para desarrollar con recarga en caliente se corre aparte:

```
cd frontend
npm install
npm run dev
```

Por defecto usa datos reales del backend (el Gateway en `http://localhost:5000`, hay que tenerlo levantado). Para trabajar con respuestas simuladas, crear el archivo `frontend/.env.development.local` (no se sube al repositorio) con una sola línea:

```
VITE_USE_MOCKS=true
```

y reiniciar `npm run dev`. Más detalle en `frontend/README.md`, sección "Datos reales o simulados".

## El flujo real, paso a paso

Es el recorrido que se muestra en la demo y el que automatiza `scripts/flujo-e2e.ps1`. Todo pasa por el Gateway (`http://localhost:5000`).

| Paso | Quién | Qué hace |
| --- | --- | --- |
| 1 | Capataz | Crea una requisición para una obra y una etapa. |
| 2 | Comprador | Aprueba la requisición. Al aprobar ve el presupuesto de la etapa (disponible y si el pedido entra). Se publica `RequisicionAprobadaEvent`. |
| 3 | (automático) | Licitaciones recibe el evento y **solo guarda** la requisición. **No crea la licitación.** |
| 4 | Comprador | **Publica la licitación a mano** (`POST /api/licitaciones`) con una fecha límite a futuro. Ahí mismo puede armar el **Magic Link** de cada proveedor y mandárselo por WhatsApp. |
| 5 | Proveedor | Ve la licitación (o abre su Magic Link) y envía su cotización. |
| 6 | Comprador | Compara las ofertas y **adjudica** una. Se emite la Orden de Compra y `OrdenCompraEmitidaEvent`. **El flujo del Comprador termina acá**: sigue la orden, pero recibir no es tarea suya. |
| 7 | (automático) | Recepción recibe la orden y la deja pendiente de recibir. |
| 8 | Proveedor | Avisa que despachó la orden. |
| 9 | Capataz | Ve las órdenes pendientes y registra la recepción (completa, parcial o con problema). |

Si el Capataz marca un **problema** en la entrega, la orden vuelve al Proveedor: el Capataz no puede recibir de nuevo hasta que el Proveedor la despache otra vez ("Volver a despachar").

Cosas que conviene saber:

- **Usuarios de prueba:** `capataz@obrasync.com`, `comprador@obrasync.com`, `proveedor@obrasync.com` y `admin@obrasync.com`. La contraseña es la que define el seed de Usuarios; no se repite acá.
- **El Proveedor solo ve una licitación si está Abierta y con la fecha límite a futuro.** Si no ve nada, casi siempre es porque el Comprador todavía no la publicó (paso 4).
- **El seed solo crea los usuarios que no existen.** Si venís de una versión vieja y el login da "credenciales inválidas", corré `docker compose down -v` para recrear la base de Usuarios y volvé a levantar.
- **Los servicios rechazan (409) requisiciones idénticas recientes.** Por eso el script cambia la cantidad en cada corrida.
- **Reglas de carga:** no se aceptan comas en los números (los decimales van con punto), las bolsas se piden en cantidades enteras y el plazo de entrega de una cotización es de **15 días como máximo**.
- **Presupuesto semilla:** al crear una obra se crea el presupuesto de cada etapa con el monto de `PRESUPUESTO_SEMILLA_POR_ETAPA` (las obras que ya existían lo reciben al reiniciar Usuarios). Con `0` no se crea nada. El monto de ejemplo lo tiene que confirmar el equipo.
- **Teléfono del proveedor:** es opcional en el registro, con código de país (por ejemplo `+54 9 11 5555 1234`). Si lo tiene, el botón de WhatsApp abre directo su chat. WhatsApp no se manda solo: la persona toca "Enviar".
- **El Comprador no recibe mercadería.** Ve el estado de la entrega, pero registrar la recepción es tarea del Capataz.

Para correr el recorrido completo contra Docker:

```powershell
.\scripts\flujo-e2e.ps1
```

## Estructura del repositorio

- `services/` un microservicio por carpeta, cada uno con su Dockerfile
- `asistente/` Asistente de IA y servidor MCP (ver `asistente/README.md`)
- `gateway/` API Gateway con YARP
- `frontend/` PWA en React
- `contracts/` contratos OpenAPI y esquemas de eventos
- `scripts/` recorridos de prueba de punta a punta y verificaciones (PowerShell)

## Correr las pruebas

- Backend (en la carpeta de cada servicio, por ejemplo `services/usuarios-obras`): `dotnet test`
- Asistente: `dotnet test` dentro de `asistente/`
- Frontend: `cd frontend` y `npm test` (las pruebas usan respuestas simuladas, no hace falta el backend)

## Gestión e higiene de secretos (#165)

> [!IMPORTANT]
> **Los secretos van en variables de entorno o user-secrets (`dotnet user-secrets`), nunca en archivos commiteados en el repositorio, Dockerfiles, Trello ni chats.**

1. **Variables de entorno y secretos locales:**
   - Crear el archivo `.env` en la raíz a partir de `.env.example`. Los archivos `.env`, `.env.*` (salvo `.env.example`) y archivos de credenciales (`credenciales*.json`, `*.pem`, `*.key`, `*.pfx`) están estrictamente ignorados en `.gitignore`.
   - Para desarrollo local en .NET sin Docker, se recomienda usar `dotnet user-secrets` en cada proyecto API.
2. **Rotación de claves de IA y APIs:**
   - Cualquier secreto o API key compartida o pegada en chats grupales (por ejemplo, la clave del gateway de IA) se considera **expuesta** y debe rotarse de inmediato, configurando el nuevo valor en la variable de entorno `AI_API_KEY` local de cada integrante.
3. **Seguridad y firma de JWT:**
   - Los tokens JWT emitidos tienen un tiempo de vida acotado y vencen solos (vida útil de 60 minutos).
   - El secreto de firma `JWT_SECRET` debe poseer una longitud mínima de 32 caracteres y configurarse exclusivamente por variables de entorno compartidas por canal seguro entre los integrantes del equipo.
4. **Entorno de ejecución (`ASPNETCORE_ENVIRONMENT`) (#169):**
   - En `docker-compose.yml`, la variable `ASPNETCORE_ENVIRONMENT` está parametrizada con valor por defecto `Development` (`${ASPNETCORE_ENVIRONMENT:-Development}`) para facilitar el desarrollo local y el acceso a Swagger.
   - Para ejecutar o probar en entornos productivos o de staging, definir `ASPNETCORE_ENVIRONMENT=Production` o `Staging` en `.env`.

## Colaboración en GitHub (#180)

- Cada Pull Request usa la plantilla de `.github/PULL_REQUEST_TEMPLATE.md`: tarjeta, Definición de Terminado y quién revisó.
- Las ramas se borran solas al mergear el PR (opción "Automatically delete head branches" de GitHub).
- Usá siempre el mismo nombre y correo en Git (`git config user.name` y `git config user.email`); el archivo `.mailmap` une las identidades viejas duplicadas.
- No agregues líneas de coautoría automáticas (`Co-Authored-By`, `Claude-Session`) en commits ni en Pull Requests.
