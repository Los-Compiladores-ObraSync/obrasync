# Cambios de la tarjeta #79: Completar Consultas de Recepciones

## 1. Resumen de la Tarjeta #79
La tarjeta **#79** ("Completar consultas de recepciones") amplía y estandariza la API del microservicio `recepcion-presupuesto` (`ObraSync.Recepcion.Api`), incorporando endpoints de consulta faltantes, soporte de filtros avanzados, serialización tipada mediante DTOs desacoplados del modelo de dominio con fechas normalizadas en UTC, y control de acceso basado en roles (RBAC).

La implementación se realizó en los siguientes pasos sucesivos:
1. **GET `/api/recepciones/{id}` (Paso 1):** Consulta de una recepción puntual por identificador (`Guid`), retornando `404 Not Found` (`ProblemDetails`) si no existe.
2. **Filtro `ordenCompraId` (Paso 2):** Inclusión del parámetro de consulta opcional `ordenCompraId` en `GET /api/recepciones`, combinable con el filtro existente `obraId`.
3. **GET `/api/recepciones/ordenes-pendientes` (Paso 3):** Endpoint para consultar órdenes de compra que aún no han sido recibidas por completo (excluye aquellas con recepción `Completa`), calculando cantidades recibidas y pendientes por cada ítem, con filtro opcional por `obraId`.
4. **Migración a DTOs en consultas GET (Paso 4):** Desacoplamiento de las entidades de dominio `Recepcion` y `DetalleRecepcion`, respondiendo `RecepcionDto` y `DetalleRecepcionDto` en `GET /api/recepciones` y `GET /api/recepciones/{id}`.
5. **Migración a DTO y normalización UTC en POST (Paso 5):** `POST /api/recepciones` (creación de recepción) pasa de responder la entidad de dominio a responder `RecepcionDto` (HTTP 201). Se normalizan las fechas (`fechaRecepcion`, `createdAt`, `updatedAt`) a UTC con indicador ISO 8601 `"Z"`.
6. **Permisos y control de acceso RBAC (Paso 6):** Configuración de permisos de lectura en `GET /api/recepciones` y `GET /api/recepciones/{id}` para los roles `Capataz`, `Comprador` y `Admin`, denegando acceso (HTTP 403 Forbidden) a roles no autorizados como `Proveedor`.

---

## 2. Endpoints del Servicio de Recepción

| Método | Ruta | Roles autorizados | Respuestas HTTP | Cuerpo / Contenido |
|---|---|---|---|---|
| `GET` | `/api/recepciones` | `Capataz, Comprador, Admin` | `200 OK`<br>`401 Unauthorized`<br>`403 Forbidden` | Array de `RecepcionDto` con sus `DetalleRecepcionDto`. Permite filtrar por `obraId` y `ordenCompraId`. |
| `GET` | `/api/recepciones/{id:guid}` | `Capataz, Comprador, Admin` | `200 OK`<br>`401 Unauthorized`<br>`403 Forbidden`<br>`404 Not Found` | Objeto `RecepcionDto` individual con sus detalles, o `ProblemDetails` con detalle `"La recepción no existe."` si el `id` no fue encontrado. |
| `GET` | `/api/recepciones/ordenes-pendientes` | `Capataz, Admin` | `200 OK`<br>`401 Unauthorized`<br>`403 Forbidden` | Array de `OrdenPendienteDto` (cada una con `items` de tipo `ItemOrdenPendienteDto` indicando `cantidadEsperada`, `cantidadRecibida` acumulada y `cantidadPendiente`). Permite filtro opcional por `obraId`. |
| `POST` | `/api/recepciones` | `Capataz, Admin` | `201 Created`<br>`400 Bad Request`<br>`401 Unauthorized`<br>`403 Forbidden`<br>`409 Conflict` | Request: `CrearRecepcionRequest`. Response (201): `RecepcionDto` de la recepción confirmada con fechas normalizadas en UTC. Publica el evento `RecepcionConfirmadaEvent` a RabbitMQ. |

---

## 3. Cambios de Contrato
- **Contrato OpenAPI (`contracts/openapi/recepciones-openapi.json`):**
  - Se agregaron las definiciones de esquemas `RecepcionDto`, `DetalleRecepcionDto`, `OrdenPendienteDto` y `ItemOrdenPendienteDto`.
  - Se actualizaron las rutas:
    - `GET /api/recepciones`: La respuesta `200` referencia `#/components/schemas/RecepcionDto` (array). Se agregaron parámetros de query `obraId` y `ordenCompraId`.
    - `GET /api/recepciones/{id}`: La respuesta `200` referencia `#/components/schemas/RecepcionDto`. Se documentó la respuesta `404` con `ProblemDetails`.
    - `GET /api/recepciones/ordenes-pendientes`: Nueva ruta documentada retornando array de `#/components/schemas/OrdenPendienteDto` y parámetro opcional `obraId`.
    - `POST /api/recepciones`: La respuesta `201` actualizó su esquema de retorno hacia `#/components/schemas/RecepcionDto`.
  - Se mantuvieron los esquemas previos `Recepcion` y `DetalleRecepcion` para retrocompatibilidad histórica.

---

## 4. Impacto en Otros Equipos
- **Frontend / UI:**
  - Debe actualizar el consumo de recepciones: los endpoints `GET /api/recepciones`, `GET /api/recepciones/{id}` y `POST /api/recepciones` ahora retornan la estructura limpia de `RecepcionDto` (sin propiedades de navegación cíclicas de EF Core).
  - Todas las marcas temporales (`fechaRecepcion`, `createdAt`, `updatedAt`) vienen expresadas en estándar UTC terminadas en `Z`.
  - Se encuentra disponible la nueva pantalla o consulta de órdenes de compra pendientes en `GET /api/recepciones/ordenes-pendientes`.
  - Aviso enviado al equipo de Frontend sobre la actualización del contrato.
- **Mensajería RabbitMQ:**
  - Sin impacto. La estructura de `RecepcionConfirmadaEvent` y su publicación en el bus de MassTransit se mantiene inalterada.
- **Infraestructura / Gateway / Base de Datos:**
  - No se requieren nuevas migraciones de base de datos ni modificaciones en variables de entorno o `docker-compose.yml`.

---

## 5. Pruebas Agregadas
Se añadieron suites de pruebas unitarias y de integración en `ObraSync.Recepcion.Tests`:

1. **`ObtenerRecepcionPorIdTests.cs` (Paso 1):**
   - `ObtenerRecepcionPorId_IdInexistente_Devuelve404`
   - `ObtenerRecepcionPorId_IdExistente_Devuelve200ConLaRecepcion`
2. **`ObtenerRecepcionesFiltrosTests.cs` (Paso 2):**
   - `ObtenerRecepciones_SinFiltroOrdenCompra_DevuelveTodas`
   - `ObtenerRecepciones_FiltrandoPorOrdenCompraId_DevuelveSoloEsaOrden`
   - `ObtenerRecepciones_FiltrandoPorObraYOrdenCompra_AplicaAmbosFiltros`
   - `ObtenerRecepciones_OrdenCompraSinRecepciones_DevuelveListaVacia`
3. **`ObtenerOrdenesPendientesTests.cs` (Paso 3):**
   - `ObtenerOrdenesPendientes_SinOrdenes_DevuelveListaVacia`
   - `ObtenerOrdenesPendientes_OrdenConRecepcionCompleta_NoLaIncluye`
   - `ObtenerOrdenesPendientes_OrdenSinRecepcion_LaIncluyeConCantidadPendienteTotal`
   - `ObtenerOrdenesPendientes_OrdenConRecepcionParcial_RestaCantidadYaRecibida`
   - `ObtenerOrdenesPendientes_FiltroPorObraId_DevuelveSoloLasDeEsaObra`
   - `ObtenerOrdenesPendientes_MultiplesRecepciones_AcumulaCantidadesRecibidas`
4. **`RecepcionesControllerDtoTests.cs` (Paso 4):**
   - `ObtenerRecepciones_DevuelveListaDeRecepcionDto`
   - `ObtenerRecepcionPorId_Existente_DevuelveRecepcionDto`
5. **`CrearRecepcionPublicacionTests.cs` y `RecepcionDtoFechasTests.cs` (Paso 5):**
   - `CrearRecepcion_Exitoso_DevuelveRecepcionDto`
   - `RecepcionDto_NormalizaFechasAUtc`
6. **`RecepcionesPermisosLecturaTests.cs` (Paso 6):**
   - `GetRecepciones_ConTokenProveedor_DebeRetornar403Forbidden`
   - `GetRecepciones_ConTokenComprador_DebeRetornar200Ok`
   - `GetRecepcionPorId_ConTokenProveedor_DebeRetornar403Forbidden`
   - `GetRecepcionPorId_ConTokenComprador_DebeRetornar200Ok`

---

## 6. Pendientes Fuera de la Tarjeta
- **Serialización de decimales (#128):** Resuelto y mergeado previamente en `main`. Garantiza que las cantidades decimales se serialicen numéricamente sin comillas en eventos y APIs.
- **Flujo de recepción parcial en frontend:** El backend ya soporta el estado y cálculo de pendientes; el frontend implementará la interfaz de recepción por remito parcial según su planificación de sprints.
