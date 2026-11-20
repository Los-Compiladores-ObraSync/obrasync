# Diseño del agente de IA de ObraSync (tarjeta #140)

Documento corto para que el equipo lo revise. Responde los 4 prerrequisitos del profe y deja registradas las decisiones del squad Frontend, Gateway y MCP (15/11/2026). Lo que sigue pendiente está marcado como **PENDIENTE**.

## 0. Qué hace el asistente
Un Capataz (o un Admin) escribe en el chat del portal y el asistente:
1. Busca insumos del catálogo ("¿hay cemento?").
2. Arma el **borrador** de una requisición ("necesito 20 bolsas de cemento para cimientos"). La requisición se crea recién cuando la persona toca **Confirmar**.
3. Se niega con claridad si le piden algo fuera de ObraSync.

## 1. Regla de oro
**El modelo nunca escribe datos.** Solo tiene herramientas de lectura y, a lo sumo, arma un borrador que viaja en el campo `borrador` de la respuesta. La escritura la hace el frontend llamando a `POST /api/requisiciones` con el JWT del usuario y una `Idempotency-Key`, cuando se toca Confirmar. Por qué: es más simple y más seguro que dejar que el modelo decida cuándo escribir, y el backend sigue siendo quien valida el permiso. Costo: el modelo no puede crear nada por su cuenta, y es justo lo que queremos.

## 2. Herramientas (prerrequisito 1)
Todas son de solo lectura, consultan al API Gateway con el JWT del usuario y se escriben una sola vez (`HerramientasCatalogo`, capa Application).

| Herramienta | Qué hace | Endpoint que llama |
|---|---|---|
| `buscar_insumos` | Busca en el catálogo por texto (máx. 5 resultados) | `GET /api/catalogo/insumos` |
| `armar_borrador_requisicion` | Recibe obra, etapa y materiales por NOMBRE; resuelve la obra y los insumos y devuelve el borrador | `GET /api/obras`, `GET /api/catalogo/insumos` |

El modelo nunca ve ni inventa ids: el servicio los resuelve. Si falta o es ambiguo un dato, devuelve un error con opciones (por ejemplo `obra_ambigua` o `etapa_requerida`) y no arma nada a medias. Máximo 10 ítems por borrador.

Próximas herramientas (tarjetas): consulta y aprobación para Compras (#141, #148, #149) y recepción y estado de requisiciones para el Capataz (#147). Todas siguen el mismo patrón: el modelo arma el borrador y solo Confirmar escribe (#146).

## 3. Framework (prerrequisito 2)
**Decisión: bucle propio para la prueba de concepto.** Microsoft Agent Framework queda como opción posterior.
- Por qué: el bucle ya está probado y con límites, tiene modelo simulado para la CI y se entiende de punta a punta. Cambiarlo ahora es reescribir algo que funciona.
- LangChain y LangGraph quedan descartados: usan Python o JavaScript, que hoy no están en el stack.
- Costo: el bucle lo mantenemos nosotros.
- Migrar a MAF después no obliga a reescribir las herramientas, porque están separadas del bucle. Antes de adoptarlo: verificar en NuGet la versión estable exacta, fijarla, confirmar que soporte .NET 8 y acordarlo con el equipo.

## 4. Modelo y API (prerrequisito 3)
- Modelo `gpt-4.1-mini` (Azure OpenAI) a través del gateway de IA de la cátedra, por Chat Completions.
- La clave y la dirección del gateway llegan solo por variables de entorno (`AI_API_KEY`, `AI_BASE_URL`). Nunca van en el repo, el Dockerfile ni el código.
- La clave del grupo tiene un cupo compartido por todo el equipo, por eso los límites de abajo.
- La CI nunca llama a Azure: las pruebas usan un modelo simulado.

## 5. Integración a la web (prerrequisito 4)
Chat del portal del Capataz (#138, React PWA) → API Gateway (`/api/asistente/chat`) → servicio del asistente → herramientas → API Gateway con el mismo JWT del usuario → servicios.
- El agente y el servidor MCP viven juntos en `asistente/` (un solo servicio, sin base de datos): puerto 5005 en local y 8080 dentro del contenedor.
- Sin estado en el servidor: el frontend manda los últimos mensajes en cada pedido. Sin streaming: el chat muestra "pensando".
- Servidor MCP en `/api/mcp` (Streamable HTTP, paquete `ModelContextProtocol.AspNetCore` fijado en 2.4.0): publica las mismas herramientas, de solo lectura, con el mismo JWT (solo Capataz y Admin).
- Contrato: `contracts/openapi/asistente-openapi.json`.

## 6. Seguridad y costo
- Siempre se usa el token del usuario, nunca una cuenta propia. Rol sin permiso: 403 y no se llama al modelo.
- Las instrucciones están acotadas a ObraSync. Lo que devuelven las APIs se trata como datos, no como órdenes (defensa contra inyección de instrucciones).
- Límites por pedido: 500 caracteres de entrada, historial de 10 mensajes, 4 vueltas, 3 llamadas a herramientas, 800 tokens de salida y 45 s en total (eran 3 y 2; adjudicar necesita listar licitaciones, listar ofertas y preparar la adjudicación en un mismo pedido, #149). Un 429 del proveedor se devuelve tal cual, sin reintentos.
- Errores en ProblemDetails con `correlationId`; se propaga `X-Correlation-ID`.

## 7. Pruebas
xUnit y NSubstitute con modelo y Gateway simulados: rol sin permiso, modelo caído, pedido fuera de tema, borrador con datos faltantes o ambiguos y endpoint del MCP. En el frontend, Vitest y React Testing Library con respuestas simuladas.

## 8. Pendiente
- **Voz (#139):** transcripción con un modelo multimodal de Azure OpenAI (recomendado) o reconocimiento de voz del navegador (plan B). Confirmar con el profe qué modelos y región tiene la clave.
- **Cupo y alertas:** confirmar con el profe cómo se mide el cupo de la clave y si se puede poner un límite de tokens por minuto y una alerta de gasto. Hasta entonces, desarrollar con el modelo simulado y avisar en el canal antes de usar el real.
- **Evaluación de 5 casos por rol (#145):** hecha. Modo automático con modelo simulado en la CI y modo manual contra el modelo real; ver `evaluacion/README.md`. Falta anotar la primera corrida real en `evaluacion/resultados/`.
- **Framework:** revisar MAF si crece la cantidad de herramientas o aparecen varios agentes.
