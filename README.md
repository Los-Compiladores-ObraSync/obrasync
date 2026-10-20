# ObraSync

Plataforma B2B de requisición, licitación y abastecimiento para obras.

## Cómo levantar todo con Docker (backend + Gateway)

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

   La primera vez tarda varios minutos (descarga imágenes y compila los 5 proyectos). SQL Server y RabbitMQ tienen que estar "healthy" antes de que arranquen los servicios.

3. **Bajar todo:**

   ```
   docker compose down
   ```

   Para borrar también los datos de las bases (empezar de cero): `docker compose down -v`.

> Si cambiás `SQL_SA_PASSWORD` después de haber levantado SQL Server una vez, hay que correr `docker compose down -v`: el volumen guarda la contraseña anterior.

## Puertos

| Qué | Puerto en tu computadora |
| --- | --- |
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

- Gateway: `http://localhost:5000/api/usuarios/login` (POST) debe llegar al servicio de Usuarios.
- Salud de los servicios: `http://localhost:5002/health` (Requisiciones), `http://localhost:5003/health` (Licitaciones) y `http://localhost:5004/health` (Recepción).
- Swagger (se activa en `Development`): `http://localhost:5001/swagger` (Usuarios), `http://localhost:5002/swagger` (Requisiciones) y `http://localhost:5004/swagger` (Recepción).
- RabbitMQ: `http://localhost:15672`.

## Frontend

El frontend todavía no está en el compose (el despliegue completo es la tarjeta #15). Se corre aparte:

```
cd frontend
npm install
npm run dev
```

Por defecto usa respuestas simuladas (`VITE_USE_MOCKS=true` en `frontend/.env.development`). Para probar contra el backend real, crear el archivo `frontend/.env.development.local` (no se sube al repositorio) con una sola línea:

```
VITE_USE_MOCKS=false
```

y reiniciar `npm run dev`. Las llamadas a `/api` se reenvían al Gateway (`http://localhost:5000`).

## Estructura del repositorio

- `services/` un microservicio por carpeta, cada uno con su Dockerfile
- `gateway/` API Gateway con YARP
- `frontend/` PWA en React
- `contracts/` contratos OpenAPI y esquemas de eventos
