# Asistente de IA de ObraSync (tarjeta #137)

Servicio que atiende el chat del asistente: recibe el mensaje del usuario y el historial reciente, le pregunta al
modelo (gpt-4.1-mini, por el gateway de IA de la cátedra) y devuelve la respuesta en texto.

**Regla de oro:** el asistente NUNCA escribe datos. Solo tiene herramientas de lectura. Si algún día arma un borrador
(requisición, aprobación…), la escritura la hace el frontend llamando al endpoint existente con el JWT del usuario
cuando éste toca Confirmar.

- Contrato: `contracts/openapi/asistente-openapi.json` (`POST /api/asistente/chat`, por el Gateway).
- Roles con acceso en esta versión: Capataz y Admin (el resto recibe 403 y no se llama al modelo).
- Herramienta disponible: `buscar_insumos` (catálogo real, vía Gateway, con el JWT del usuario).
- Puerto: 5005 en tu computadora (propuesto; todavía hay que acordarlo con el equipo). Dentro del contenedor, 8080.

## Capas
`Api` (controlador, JWT, errores ProblemDetails) · `Application` (el agente, sus límites y la herramienta) ·
`Infrastructure` (cliente del gateway de IA y buscador del catálogo). No hay capa `Domain` porque el servicio no
tiene entidades ni base de datos.

## Límites del agente (para no gastar de más: el cupo es compartido por todo el grupo)
500 caracteres por mensaje · historial de 10 mensajes · 3 vueltas · 2 llamadas a herramientas · 300 tokens de
salida · 45 s en total. Un `429` del proveedor se devuelve tal cual, sin reintentos.

## Configuración (variables de entorno, nunca en el código ni en el repo)
| Variable | Para qué |
|---|---|
| `JWT_SECRET` (`Jwt__Secret`) | El mismo secreto que usa Usuarios para firmar. |
| `AI_API_KEY` (`Ai__ApiKey`) | Clave del gateway de IA. Sin ella el chat responde 503. |
| `AI_BASE_URL` (`Ai__BaseUrl`) | Dirección del gateway de IA (está en la tarjeta #137). Sin ella el chat responde 503. |
| `AI_DEPLOYMENT` (`Ai__Deployment`) | Opcional. Por defecto `gpt-4.1-mini`. |
| `Gateway__BaseUrl` | Dónde está el API Gateway de ObraSync (en Docker: `http://gateway:8080`; por defecto `http://localhost:5000`). |

## Correrlo
- Con todo el sistema: poné `AI_API_KEY` y `AI_BASE_URL` en tu `.env` y corré `docker compose up -d --build`.
- Solo este servicio desde Visual Studio: guardá la clave con `dotnet user-secrets set "Ai:ApiKey" "<clave>"`
  (en la carpeta `ObraSync.Asistente.Api`) o definí `AI_API_KEY` y `AI_BASE_URL` en tu terminal.

## Pruebas
`dotnet test asistente/ObraSync.Asistente.Tests/ObraSync.Asistente.Tests.csproj`. Usan un modelo simulado y un
Gateway simulado: no gastan cupo ni necesitan nada levantado (la CI nunca llama a Azure).
