# Contrato Tiempo Real — Hub SignalR de Chat por Orden de Compra (#218)

Este documento define el contrato del Hub SignalR expuesto por **Licitaciones y Cotizaciones** para notificaciones en tiempo real sobre el chat y estado de problemas de una Orden de Compra.

> **Regla de arquitectura:** El Hub es **únicamente de lectura/suscripción en tiempo real (push)**. Toda escritura (enviar mensaje, marcar leído o resolver problema) se realiza exclusivamente por los endpoints REST documentados en `contracts/openapi/licitaciones-cotizaciones-openapi.json`.

---

## 1. Endpoint y Enrutamiento

- **Microservicio (`licitaciones-cotizaciones`):** `/api/ordenes-compra/chat-hub` (puerto local `5003` / contenedor `8080`).
- **A través del API Gateway (`YARP`, puerto `5000`):** `/api/ordenes-compra/chat-hub` (enrutado automáticamente por la regla `/api/ordenes-compra/{**resto}` sin configuración adicional).
- **Protocolo:** ASP.NET Core SignalR (JSON en `camelCase`, soporta WebSockets, Server-Sent Events y Long Polling).

---

## 2. Autenticación y Permisos

- **JWT por Query String (`access_token`):** Siguiendo el estándar del cliente `@microsoft/signalr` para WebSockets, el cliente envía el JWT como parámetro `?access_token=<jwt>` en `/api/ordenes-compra/chat-hub/negotiate` y en la conexión del socket.
- **Protección de logs (seguridad):** Un middleware temprano de la API extrae `access_token` a `HttpContext.Items` y lo **remueve de `Request.QueryString`** antes de routing, autenticación y manejadores de errores; además `Microsoft.AspNetCore.Hosting.Diagnostics` está configurado en `Warning` para que el token jamás se escriba en los logs del servidor.
- **Roles habilitados:** `Comprador`, `Admin` y `Proveedor` (solo titular de la orden: `sub == ordenCompra.ProveedorId`).
- Cualquier otro rol (por ejemplo `Capataz`) o un `Proveedor` distinto al titular recibe rechazo en la negociación/conexión (`401`/`403`) o `HubException` al intentar unirse al grupo de la orden.

---

## 3. Grupos y Métodos Cliente → Servidor

Los clientes conectados se suscriben por orden de compra al grupo con nombre exacto:

```text
orden-{ordenCompraId}
```
*(GUID en formato estándar en minúsculas con guiones, ej. `orden-3fa85f64-5717-4562-b3fc-2c963f66afa6`).*

### `UnirseAOrden(ordenCompraId: Guid): Task`
Suscribe la conexión actual al grupo `orden-{ordenCompraId}` después de validar:
1. Que `ordenCompraId` corresponda a una orden existente.
2. Que el usuario tenga rol `Comprador`, `Admin`, o sea el `Proveedor` titular (`sub == orden.ProveedorId`).

Si no existe o no tiene permiso, el Hub rechaza la invocación lanzando `HubException`.

### `SalirDeOrden(ordenCompraId: Guid): Task`
Remueve la conexión actual del grupo `orden-{ordenCompraId}`.

---

## 4. Eventos Push Servidor → Cliente (Grupo `orden-{ordenCompraId}`)

### 1) `MensajeRecibido`
Se emite al guardar un mensaje humano vía `POST /api/ordenes-compra/{id}/chat/mensajes` **y** al generarse cualquiera de los 3 mensajes automáticos del sistema:
- `"El Proveedor despachó la orden"` (al despachar la orden por primera vez)
- `"El Capataz registró un problema: {motivo}"` (al consumir `RecepcionConProblemaEvent`)
- `"Llegó todo el pedido"` (al consumir `RecepcionConfirmadaEvent` válida con `ordenCompleta == true`)

**Payload (`MensajeChatOrdenDto`):**
```json
{
  "id": "b1f2c3d4-0000-4000-8000-000000000001",
  "ordenCompraId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "autorId": "7b2e1a10-1111-4000-8000-000000000002",
  "autorNombre": "Juan Pérez",
  "autorRol": "Comprador",
  "esSistema": false,
  "texto": "¿Confirmás si sale el camión antes del mediodía?",
  "fechaCreacion": "2026-11-21T20:30:00Z"
}
```
*(En mensajes automáticos del sistema: `autorId` es `null`, `autorNombre` es `"Sistema"`, `autorRol` es `"Sistema"` y `esSistema` es `true`).*

### 2) `LecturaActualizada`
Se emite cuando un participante habilitado marca la orden como leída mediante `POST /api/ordenes-compra/{id}/chat/marcar-leido`.

**Payload (`LecturaChatResponseDto`):**
```json
{
  "ordenCompraId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "usuarioId": "7b2e1a10-1111-4000-8000-000000000002",
  "ultimaLecturaEn": "2026-11-21T20:31:00Z"
}
```

### 3) `ProblemaEstadoActualizado`
Se emite cuando se abre un problema (`RecepcionConProblemaEvent`), cuando se cierra automáticamente por una recepción válida (`RecepcionConfirmadaEvent` no rechazada) o cuando `Comprador`/`Admin` lo resuelve manualmente mediante `POST /api/ordenes-compra/{id}/problema/resolver`.

**Payload (`ProblemaOrdenEstadoDto`):**
```json
{
  "ordenCompraId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tieneProblemaAbierto": false,
  "motivoProblemaAbierto": "Llegó dañada",
  "fechaProblemaAbierto": "2026-11-21T19:00:00Z",
  "problemaResueltoEn": "2026-11-21T20:35:00Z",
  "problemaResueltoPor": "7b2e1a10-1111-4000-8000-000000000002"
}
```
*(Cuando el cierre es automático por recepción válida en obra, `problemaResueltoPor` es `null`).*
