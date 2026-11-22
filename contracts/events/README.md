# Eventos de integración (RabbitMQ + MassTransit)

Los esquemas JSON de esta carpeta son el contrato: si un resumen o un comentario dice otra cosa, manda el esquema.

## Convención para los números decimales (decisión de la tarjeta #128)

- Los campos decimales (cantidades, precios, montos) viajan como **número JSON** (`10.5`), tal como dicen los esquemas (`"type": "number"`). No se cambian los esquemas.
- **Por qué hace falta configurarlo:** MassTransit 8 escribe por defecto los `decimal` como texto (`"10.5"`) para no perder precisión. Cada servicio que publica o consume eventos le saca ese convertidor al configurar el bus.
- **Cómo:** `cfg.UsarDecimalesComoNumero()` al configurar el bus (RabbitMQ y en memoria). Hay una copia idéntica de la extensión en Licitaciones y en Requisiciones (`Infrastructure/Messaging/ConfiguracionJsonEventos.cs`); Recepción tiene la misma lógica escrita directamente en su `Program.cs`.
- **Lectura tolerante:** los consumidores siguen leyendo número **y** texto (`NumberHandling = AllowReadingFromString`), para no romperse si un publicador viejo todavía manda texto.
- **Costo:** un decimal con muchísimos dígitos puede perder precisión si lo lee un sistema que lo convierte a `double`. Entre servicios .NET (que leen a `decimal`) no pasa.
- **Pruebas:** cada servicio publica un evento por un bus de prueba y comprueba que el cuerpo sale con números (`EventosJsonTests`; en Recepción, `BusSerializacionTests`).

## Campos obligatorios en todos los eventos

`eventId` (GUID, los consumidores lo usan para ser idempotentes), `occurredAt` (UTC) y `correlationId`.

## Esquemas publicados en esta carpeta

- `RequisicionAprobadaEvent.json`: emitido por Requisiciones y Catálogo al aprobar una requisición; consumido por Licitaciones y Cotizaciones, y por Usuarios y Obras para notificaciones push (#227).
- `OrdenCompraEmitidaEvent.json`: emitido por Licitaciones y Cotizaciones al adjudicar y emitir una orden de compra; consumido por Recepción y Control Presupuestario, y por Usuarios y Obras para notificaciones push (#227).
- `OrdenCompraDespachadaEvent.json`: emitido por Licitaciones y Cotizaciones al marcar una orden de compra como despachada; consumido por Recepción y Control Presupuestario (#202, #216, #217) y por Usuarios y Obras para notificaciones push (#227). Incluye campos aditivos opcionales `fechaEstimadaLlegada` y `observacionDespacho`. Su registro espejo en C# es `OrdenCompraDespachadaEvent.cs`.
- `OrdenCompraBloqueadaEvent.json`: evento de auditoría emitido por Recepción y Control Presupuestario cuando una orden de compra supera la partida presupuestaria de la etapa/obra y queda en estado `Bloqueada` (#95, #154).
- `RecepcionConfirmadaEvent.json`: emitido por Recepción y Control Presupuestario al confirmar una recepción física válida (Parcial o Completa) de materiales en obra; consumido por Licitaciones (#222, #218) y por Usuarios y Obras para notificaciones push (#227). Incluye el campo aditivo `ordenCompleta` (#217, #222) para indicar si la orden quedó recibida en su totalidad.
- `RecepcionConProblemaEvent.json`: emitido por Recepción y Control Presupuestario cuando el Capataz registra una entrega con problema en estado `Rechazada` (#217). Lleva `motivo`, `detalle`, `cantidadFotos` (sin adjuntar binarios), `obraId` y `proveedorId`. Consumido por Licitaciones (#218) y Usuarios (#227). Su registro espejo en C# es `RecepcionConProblemaEvent.cs`.
- `MensajeChatEnviadoEvent.json`: emitido por Licitaciones y Cotizaciones cuando un usuario (`Comprador`, `Admin` o `Proveedor` dueño) envía un mensaje en el chat de una Orden de Compra (#218); consumido por Usuarios y Obras para notificaciones (#227). Lleva `remitente` (`usuarioId`, `nombre`, `rol`), `destinatarios` (`usuarioIds` con `[proveedorId]` cuando escribe Compras/Admin; `roles` con `["Comprador", "Admin"]` cuando escribe el Proveedor) y `fragmento` (máx. 120 caracteres). **Nunca** se emite para mensajes automáticos del sistema. Su registro espejo en C# es `MensajeChatEnviadoEvent.cs`.

## Regla de orden y reintentos entre eventos de Orden de Compra

En mensajería asíncrona (RabbitMQ + MassTransit), dos eventos distintos no tienen garantía estricta de orden global: `OrdenCompraDespachadaEvent` puede arribar a Recepción y Control Presupuestario **antes** de que se haya procesado `OrdenCompraEmitidaEvent` (por ejemplo, si el despacho ocurre inmediatamente después de adjudicar o ante un reintento en colas distintas).

- **Comportamiento obligatorio en Recepción (#204):** si al consumir `OrdenCompraDespachadaEvent` todavía no existe la orden de compra (`ordenCompraId`) en `DB_Recepcion`, el consumidor **no debe descartar el mensaje ni registrar el `eventId` en `EventosProcesados`**. Debe fallar lanzando una excepción para que la política de reintentos de MassTransit vuelva a entregarlo hasta que `OrdenCompraEmitidaEvent` haya creado la orden.
- Una vez persistida la orden en `DB_Recepcion`, el consumidor debe registrar el `eventId` en `EventosProcesados` y aplicar el estado de despacho de forma idempotente.
