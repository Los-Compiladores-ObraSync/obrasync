# Cambios de la tarjeta #79 (paso 1): GET /api/recepciones/{id}

## Resumen
Tarjeta #79 (completar consultas de recepciones), paso 1. Se agregó el endpoint `GET /api/recepciones/{id}`, que devuelve una recepción con sus detalles o un 404 si no existe. También se corrigió un problema de formato al final de `RecepcionesController.cs`. No se tocó ningún otro endpoint, servicio, contrato ni configuración.

## Cambios por archivo
- `ObraSync.Recepcion.Api/Controllers/RecepcionesController.cs`
  - Nuevo método `ObtenerRecepcionPorId`, ubicado justo después de `ObtenerRecepciones`. Busca la recepción por `Id` incluyendo `Detalles`, en modo solo lectura (`AsNoTracking`). Si no existe responde 404 con `Problem(statusCode: 404, detail: "La recepción no existe.")`, el mismo estilo que usan los demás métodos.
  - Corrección de formato: el cierre de `GetCorrelationIdFromHeader` y el de la clase estaban en la misma línea (`}}`). Ahora están en líneas separadas. No hay cambio de comportamiento.
- `ObraSync.Recepcion.Tests/ObtenerRecepcionPorIdTests.cs` (nuevo): pruebas del endpoint. Ver "Pruebas agregadas".
- `CAMBIOS_TARJETA_79.md` (nuevo): este documento.

## Endpoints afectados
| Método | Ruta | Respuesta | Cuerpo |
|---|---|---|---|
| GET | `/api/recepciones/{id}` (`id` es un GUID) | 200 | La entidad `Recepcion` con su colección `Detalles`, igual que en el listado. |
| GET | `/api/recepciones/{id}` | 404 | `ProblemDetails` con `detail: "La recepción no existe."`. |

- El endpoint hereda `[Authorize]` del controller: requiere un token válido. No exige rol específico, igual que `GET /api/recepciones`.
- Un `id` que no es un GUID válido no coincide con la ruta (restricción `{id:guid}`) y ASP.NET responde 404 antes de entrar al método.
- `GET /api/recepciones` (`ObtenerRecepciones`) no cambió, ni en firma ni en respuesta.

## Cambios de contrato
Ninguno en este paso. No se modificaron eventos, esquemas de `contracts/` ni DTOs.

## Impacto en otros equipos
Ninguno en este paso. Es un endpoint nuevo y aditivo. No se modificó ningún otro servicio, el gateway, `docker-compose.yml` ni `.env`.

## Pruebas agregadas
Archivo `ObtenerRecepcionPorIdTests.cs`. Usan un `DbContext` con base InMemory y un publisher simulado con NSubstitute, el mismo patrón que `RecepcionesControllerTests`. (`Include`, `AsNoTracking` y `FirstOrDefaultAsync` no se pueden simular con NSubstitute, por eso se usa InMemory.)
- `ObtenerRecepcionPorId_IdInexistente_Devuelve404`: con una base que tiene otra recepción, pedir un id aleatorio devuelve un `ObjectResult` con `StatusCode` 404 y `ProblemDetails.Detail` igual a "La recepción no existe.".
- `ObtenerRecepcionPorId_IdExistente_Devuelve200ConLaRecepcion`: pedir el id de una recepción existente (hay otra en la base) devuelve un `OkObjectResult` con la recepción correcta (mismo `Id`, `NumeroRemito` y un detalle incluido).

## Pendientes
- **Paso a DTO:** el endpoint devuelve la entidad, igual que el listado. Se migrará a DTO después para toda la lista, para no mezclar formatos de respuesta.
- **Filtro `ordenCompraId`** en `GET /api/recepciones`: no se hizo en este paso.
- **Consulta de órdenes de compra pendientes:** no se hizo en este paso.
- **Decisión de serialización de #78:** sigue pendiente. No se tocó nada del evento ni de la configuración de MassTransit.
- **Verificación en Docker/RabbitMQ:** no se hizo en este paso.

