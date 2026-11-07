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
