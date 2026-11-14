# ObraSync

Plataforma B2B de requisición, licitación y abastecimiento para obras.

## Cómo levantar todo con Docker (backend + Gateway + frontend)

Requisitos: Docker Desktop abierto y funcionando.

1. **Crear el archivo `.env`** en la raíz del repositorio, a partir del ejemplo:

   ```
   copy .env.example .env
   ```

   Abrirlo y completar `SQL_SA_PASSWORD` y `JWT_SECRET`. El `JWT_SECRET` tiene que ser **el mismo para todo el equipo** (pedirlo por el grupo del equipo). El archivo `.env` no se sube al repositorio.

2. **Levantar:**

   ```
   docker compose up --build
   ```

   La primera vez tarda varios minutos (descarga imágenes y compila los 4 servicios, el Gateway y el frontend). SQL Server y RabbitMQ tienen que estar "healthy" antes de que arranquen los servicios.

3. **Bajar todo:**

   ```
   docker compose down
   ```

   Para borrar también los datos de las bases (empezar de cero): `docker compose down -v`.

> Si cambiás `SQL_SA_PASSWORD` después de haber levantado SQL Server una vez, hay que correr `docker compose down -v`: el volumen guarda la contraseña anterior.

## Puertos

| Qué | Puerto en tu computadora |
| --- | --- |
| Frontend (provisional, pendiente de acuerdo del equipo) | 5173 |
| API Gateway (la puerta de entrada) | 5000 |
| Usuarios y Obras | 5001 |
| Requisiciones y Catálogo | 5002 |
| Licitaciones y Cotizaciones | 5003 |
| Recepción y Presupuesto | 5004 |
| SQL Server | 1433 |
| RabbitMQ (servicios) | 5672 |
| RabbitMQ (panel web, usuario y contraseña `guest`) | 15672 |

Dentro de Docker cada servicio escucha en el 8080; los puertos de la tabla son los de afuera.

## Probar que está andando

- Frontend: `http://localhost:5173` debe mostrar el login.
- Gateway: `http://localhost:5000/api/usuarios/login` (POST) debe llegar al servicio de Usuarios.
- Salud de los servicios: `http://localhost:5002/health` (Requisiciones), `http://localhost:5003/health` (Licitaciones) y `http://localhost:5004/health` (Recepción).
- Swagger (se activa en `Development`): `http://localhost:5001/swagger` (Usuarios), `http://localhost:5002/swagger` (Requisiciones) y `http://localhost:5004/swagger` (Recepción).
- RabbitMQ: `http://localhost:15672`.

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
| 2 | Comprador | Aprueba la requisición. Se publica `RequisicionAprobadaEvent`. |
| 3 | (automático) | Licitaciones recibe el evento y **solo guarda** la requisición. **No crea la licitación.** |
| 4 | Comprador | **Publica la licitación a mano** (`POST /api/licitaciones`) con una fecha límite a futuro. |
| 5 | Proveedor | Ve la licitación y envía su cotización. |
| 6 | Comprador | Compara las ofertas y **adjudica** una. Se emite la Orden de Compra y `OrdenCompraEmitidaEvent`. |
| 7 | (automático) | Recepción recibe la orden y la deja pendiente de recibir. |
| 8 | Capataz | Ve las órdenes pendientes y registra la recepción. |

Cosas que conviene saber:

- **Usuarios de prueba:** `capataz@obrasync.com`, `comprador@obrasync.com`, `proveedor@obrasync.com` y `admin@obrasync.com`. La contraseña es la que define el seed de Usuarios; no se repite acá.
- **El Proveedor solo ve una licitación si está Abierta y con la fecha límite a futuro.** Si no ve nada, casi siempre es porque el Comprador todavía no la publicó (paso 4).
- **El seed solo crea los usuarios que no existen.** Si venís de una versión vieja y el login da "credenciales inválidas", corré `docker compose down -v` para recrear la base de Usuarios y volvé a levantar.
- **Los servicios rechazan (409) requisiciones idénticas recientes.** Por eso el script cambia la cantidad en cada corrida.

Para correr el recorrido completo contra Docker:

```powershell
.\scripts\flujo-e2e.ps1
```

## Estructura del repositorio

- `services/` un microservicio por carpeta, cada uno con su Dockerfile
- `gateway/` API Gateway con YARP
- `frontend/` PWA en React
- `contracts/` contratos OpenAPI y esquemas de eventos