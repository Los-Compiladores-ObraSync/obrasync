# Asistente de IA y servidor MCP de ObraSync (tarjetas #137, #100 y #98)

Este proyecto hace DOS cosas (acordado en #137/#98: el agente vive dentro del proyecto del MCP, sin un servicio más):
1. **Chat del asistente** (`POST /api/asistente/chat`): recibe el mensaje del usuario y le pregunta al modelo.
2. **Servidor MCP** (`/api/mcp`, Streamable HTTP): expone las mismas herramientas a cualquier asistente de IA compatible.
Las herramientas se escriben UNA sola vez (`HerramientasCatalogo`, capa Application): el agente las usa directo y el MCP
las publica sin duplicar lógica.

Servicio que atiende el chat del asistente: recibe el mensaje del usuario y el historial reciente, le pregunta al
modelo (gpt-5-mini en el gateway nuevo de la cátedra; gpt-4.1-mini en el anterior) y devuelve la respuesta en texto.

**Regla de oro:** el asistente NUNCA escribe datos. Solo tiene herramientas de lectura. A lo sumo ARMA un borrador
(tarjeta #100: una requisición) que viaja en el campo `borrador` de la respuesta. La escritura la hace el frontend
llamando a `POST /api/requisiciones` con el JWT del usuario y una `Idempotency-Key`, cuando la persona toca Confirmar.
El borrador no se guarda en ninguna base.

- Contrato: `contracts/openapi/asistente-openapi.json` (`POST /api/asistente/chat`, por el Gateway).
- Roles con acceso: Capataz (arma borradores de requisición), Comprador (consulta requisiciones, solo lectura) y Admin (ambos). El resto (Proveedor) recibe 403 y no se llama al modelo.
- Herramientas (todas de lectura, vía Gateway, con el JWT del usuario):
  - `buscar_insumos`: busca en el catálogo real.
  - `armar_borrador_requisicion`: recibe obra, etapa y materiales por NOMBRE; el servicio resuelve la obra
    (`GET /api/obras`, que ya filtra por rol) y los insumos (`GET /api/catalogo/insumos`), así el modelo nunca
    inventa ids. Si falta o es ambiguo un dato, devuelve un error con opciones (`obra_requerida`, `obra_ambigua`,
    `etapa_requerida`, `insumo_ambiguo`, `insumo_no_encontrado`, `cantidad_invalida`, `items_invalidos`…) y no
    arma nada a medias. Máximo 10 ítems por borrador.
- Puerto: 5005 en tu computadora (acordado el 15/11). Dentro del contenedor, 8080.
- Diseño y decisiones: `asistente/diseno-agente.md` (tarjeta #140).

## Servidor MCP (#98)
- Dirección: `POST http://localhost:5000/api/mcp` (por el Gateway; internamente `/api/mcp` en este servicio).
- Paquete: `ModelContextProtocol.AspNetCore` **2.4.0** (versión fija; última estable al 15/11/2026, compatible con .NET 8).
  Para actualizarlo hay que acordarlo con el equipo y revisar las notas de la versión.
- Modo sin sesiones (`Stateless`): cada pedido es independiente.
- Autenticación: el cliente manda `Authorization: Bearer <JWT>` en cada pedido. Capataz, Comprador y Admin (el resto recibe 403).
  Las herramientas consultan al Gateway con ESE token, nunca con una cuenta propia: cada cliente ve lo que su rol permite.
- Herramientas: las mismas dos de arriba, marcadas como solo lectura. No necesita el modelo de IA ni la clave de Azure.
- Probarlo a mano con un cliente MCP de prueba, por ejemplo MCP Inspector (`npx @modelcontextprotocol/inspector`):
  transporte "Streamable HTTP", URL `http://localhost:5000/api/mcp` y el encabezado `Authorization` con el token
  de un Capataz (login en `POST /api/usuarios/login`).

## Herramientas generadas desde el contrato (#151)
Un generador propio en C# (`Application/Generadas`) lee los `*-openapi.json` de `contracts/openapi` y arma una herramienta de
**solo lectura** por cada `GET`. Nombre, descripción y parámetros salen del contrato; si el contrato cambia, las herramientas cambian.
- **Apagado por defecto** (`HERRAMIENTAS_GENERADAS=false`): ofrecerlas suma definiciones a cada pedido y gasta el cupo compartido de tokens.
  Para probarlas: poné `HERRAMIENTAS_GENERADAS=true` en el `.env` y `docker compose up -d --build asistente-api`.
- Los contratos se montan en el contenedor (solo lectura) desde `contracts/openapi`; si cambian, reiniciá el contenedor.
- Nombres: `consultar_` + la ruta (ej. `consultar_usuarios_proveedores`, `consultar_recepciones_por_id`).
- Roles: se sacan de "(Roles: ...)" del resumen o de "Roles permitidos: ..." de la descripción. Si el contrato no los declara, queda solo Admin.
  Cada rol recibe solo lo que el contrato le permite, y al ejecutar se vuelve a verificar. El permiso real lo sigue validando cada API con el JWT del usuario.
- Se saltean los GET que ya tienen herramienta escrita a mano (devuelven respuestas más cortas) y los que no deben llegar al modelo
  (por ejemplo el Magic Link, que es un secreto). La lista y el motivo están en `GeneradorHerramientasOpenApi.Excluidos`.
- Validación antes de llamar: argumentos desconocidos, tipos, rangos, enums y UUID según el contrato; texto libre corto y sin `/ ? & ..`;
  `tamano` por defecto 10 y tope 20. Respuesta máxima 12 KB (unos 3.000 tokens: el cupo es de 10.000 por minuto), timeout 3 s, errores con código estable. La URL sale de `Gateway:BaseUrl`.
- Alcance de esta primera versión: lo usa el agente del chat. Exponerlas también en el servidor MCP queda como paso siguiente
  (hay que registrar herramientas de forma dinámica en el SDK).

## Capas
`Api` (controlador, JWT, errores ProblemDetails) · `Application` (el agente, sus límites y la herramienta) ·
`Infrastructure` (cliente del gateway de IA y lectores de catálogo y obras). No hay capa `Domain` porque el servicio no
tiene entidades ni base de datos.

## Límites del agente (para no gastar de más: el cupo es compartido por todo el grupo)
500 caracteres por mensaje · historial de 10 mensajes · 4 vueltas · 3 llamadas a herramientas · 800 tokens de
salida · 45 s en total. Un `429` del proveedor se devuelve tal cual, sin reintentos.

## Configuración (variables de entorno, nunca en el código ni en el repo)
| Variable | Para qué |
|---|---|
| `JWT_SECRET` (`Jwt__Secret`) | El mismo secreto que usa Usuarios para firmar. |
| `AI_API_KEY` (`Ai__ApiKey`) | Clave del gateway de IA. Sin ella el chat responde 503. |
| `AI_BASE_URL` (`Ai__BaseUrl`) | Dirección del gateway de IA (está en la tarjeta #137). Sin ella el chat responde 503. |
| `AI_DEPLOYMENT` (`Ai__Deployment`) | Modelo del chat. Por defecto `gpt-4.1-mini`. Con el gateway nuevo (#211): `shared-unaj-newsku-resource/gpt-5-mini`. |
| `Ai__ReasoningEffort` | Opcional. Esfuerzo de razonamiento de los modelos gpt-5 (por defecto `minimal`; `none` para no mandarlo). Con un tope de tokens de salida bajo, un razonamiento largo deja la respuesta vacía. |

**Dos estilos de gateway (#211).** El servicio mira cómo termina `AI_BASE_URL`: si termina en `/v1` usa el estilo OpenAI (URL `.../chat/completions`, modelo en el cuerpo, clave como `Authorization: Bearer`); si no, el estilo Azure de antes (`/openai/deployments/{deployment}/chat/completions?api-version=...`, clave en `api-key`).
| `Gateway__BaseUrl` | Dónde está el API Gateway de ObraSync (en Docker: `http://gateway:8080`; por defecto `http://localhost:5000`). |

## Correrlo
- Con todo el sistema: poné `AI_API_KEY` y `AI_BASE_URL` en tu `.env` y corré `docker compose up -d --build`.
- Solo este servicio desde Visual Studio: guardá la clave con `dotnet user-secrets set "Ai:ApiKey" "<clave>"`
  (en la carpeta `ObraSync.Asistente.Api`) o definí `AI_API_KEY` y `AI_BASE_URL` en tu terminal.

## Pruebas
`dotnet test asistente/ObraSync.Asistente.Tests/ObraSync.Asistente.Tests.csproj`. Usan un modelo simulado y un
Gateway simulado: no gastan cupo ni necesitan nada levantado (la CI nunca llama a Azure).
