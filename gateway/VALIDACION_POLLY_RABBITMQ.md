# Validación de Polly (Gateway) y RabbitMQ de punta a punta (tarjeta #124)

Este documento es el procedimiento para la demo y para el ensayo. Está pensado para correrlo en tu computadora con Docker Desktop.
Los resultados se anotan en la tabla del final (y en la tarjeta #124 de Trello).

> Antes de empezar: tener `.env` en la raíz con `SQL_SA_PASSWORD` y `JWT_SECRET` (ver `.env.example`) y Docker Desktop abierto.

---

## Parte A. Polly en el Gateway (reintentos con retroceso y circuit breaker)

### Qué hace el Gateway (valores de `gateway/src/ObraSync.Gateway/appsettings.json`, sección `Resiliencia`)
- **Solo los GET se reintentan** (hasta 3 veces, esperando 200 ms, 400 ms, 800 ms más un poco al azar). Un POST no se reintenta porque podría duplicar una requisición o una orden de compra.
- **Circuit breaker por servicio:** si falla el 50 % de los pedidos (mínimo 5 pedidos en 30 s), se "abre" y durante 15 s responde enseguida sin llamar al servicio. Después deja pasar un pedido de prueba: si anda, se cierra.
- Cuando el servicio no responde, el cliente recibe **502 con ProblemDetails** (`type`, `title`, `status`, `detail`, `instance` y `correlationId`), nunca un cuelgue.
- Qué cuenta como falla: no poder conectarse o recibir 502/503/504. Un 500 **no** cuenta (es un error del código del servicio y repetir no lo arregla).

### A.1 Prueba automática (no necesita Docker)
```powershell
cd gateway\src\ObraSync.Gateway.Tests
dotnet test --filter "FullyQualifiedName~Resiliencia"
```
Prueba, pasando por el Gateway completo: GET reintentado 3 veces y 502 con ProblemDetails y `correlationId`; POST sin reintento; breaker que se abre y deja de llamar al servicio; un servicio caído que no afecta a los demás; y recuperación cuando el servicio vuelve.

### A.2 Prueba manual con Docker (lo que se muestra en la demo)
1. Levantar todo: `docker compose up -d --build` y esperar a que `docker compose ps` muestre los servicios en marcha.
2. Comprobar el Gateway: `curl.exe -i http://localhost:5000/health` (debe dar 200).
3. Apagar un servicio: `docker compose stop requisiciones-catalogo-api`.
4. Pedir algo de ese servicio **varias veces seguidas** (por lo menos 6):
   ```powershell
   1..6 | ForEach-Object { curl.exe -s -i http://localhost:5000/api/requisiciones/prueba; "" }
   ```
   **Esperado:** todas las respuestas son `502` con `Content-Type: application/problem+json` y un `correlationId`. Las primeras pueden tardar un poquito; después del breaker abierto responden al instante.
   Nota: con el contenedor apagado el nombre del servicio deja de existir en la red de Docker ("Name or service not known"); esos errores **no se reintentan** (repetir no lo arregla), pero **sí cuentan** para abrir el breaker.
5. Confirmar que el resto sigue andando: `curl.exe -i http://localhost:5000/health` y un pedido a otro servicio (por ejemplo, entrar al login desde la PWA).
6. Ver el rastro: `docker compose logs gateway --tail 50` (buscar el `correlationId` de una respuesta).
7. Volver a prender el servicio: `docker compose start requisiciones-catalogo-api`, esperar unos 20 s (pausa del breaker + arranque) y repetir el pedido. **Esperado:** vuelve a responder normal (el servicio puede devolver 401 sin token: eso ya significa que llegó).

---

## Parte B. RabbitMQ y los eventos de punta a punta

Recorrido: `RequisicionAprobadaEvent` (Requisiciones -> Licitaciones) y `OrdenCompraEmitidaEvent` (Licitaciones -> Recepción).
`RecepcionConfirmadaEvent` se publica desde Recepción; todavía no tiene consumidor en otro servicio.

### B.1 Preparación
1. `docker compose up -d --build` (RabbitMQ, SQL Server, los 4 servicios y el Gateway).
2. Panel de RabbitMQ: http://localhost:15672 (usuario y clave: `guest` / `guest`, o los de `RABBITMQ_USER` y `RABBITMQ_PASS` del `.env`).
3. En la pestaña **Queues and Streams** tienen que aparecer las colas de los consumidores (la de Licitaciones para `RequisicionAprobadaEvent` y la de Recepción para `OrdenCompraEmitidaEvent`), cada una con **Consumers = 1**.
   Si una cola no aparece o tiene 0 consumidores, ese servicio no se conectó a RabbitMQ: anotarlo como falla (revisar `RabbitMQ__Host` de ese servicio en el compose y `docker compose logs <servicio>`).

### B.2 Recorrido completo
Usar la PWA (frontend conectado al Gateway, sin mocks) o los endpoints por el Gateway:
1. **Capataz** crea una requisición. **Comprador** la aprueba -> se publica `RequisicionAprobadaEvent`.
2. En Licitaciones debe existir la requisición aprobada lista para licitar. Publicar la licitación, cotizar como **Proveedor** y **adjudicar** como Comprador -> se emite la OC y se publica `OrdenCompraEmitidaEvent`.
3. En Recepción, la OC debe figurar como pendiente de recibir (pantalla de recepción del celular).
4. En el panel de RabbitMQ, en cada cola: **Ready = 0** y **Unacked = 0** (se consumió todo) y en "Message rates" se ven los *ack*.
5. **No debe existir una cola terminada en `_error`** con mensajes: si existe, un consumidor falló y el mensaje fue a parar ahí. Anotarlo como falla y abrir el mensaje para ver el motivo.

### B.3 Los decimales viajan como número (tarjeta #128)
1. Apagar Recepción: `docker compose stop recepcion-presupuesto-api`.
2. Repetir el paso 2 de B.2 hasta adjudicar. El mensaje queda esperando en la cola de Recepción (**Ready = 1**). Esto también prueba que **no se pierden mensajes si el consumidor está caído**.
3. En el panel: cola de Recepción -> **Get messages** -> Ack mode `Nack message requeue true`, Messages `1` -> **Get Message(s)**.
4. **Esperado:** en el cuerpo, `"montoTotal":1234.56`, `"cantidad":10.5` y `"precioUnitario":...` aparecen **sin comillas** (número). Si aparecen entre comillas, falla (el publicador de Licitaciones no tiene la configuración).
5. Copiar el cuerpo completo (lo vas a usar en B.4) y volver a prender: `docker compose start recepcion-presupuesto-api`. **Esperado:** la cola se vacía sola.

### B.4 Idempotencia (el mismo evento dos veces no duplica el efecto)
1. En el panel: **Exchanges** -> el exchange del evento (nombre parecido a `ObraSync.Contracts.Events:OrdenCompraEmitidaEvent`) -> **Publish message**.
2. En *Payload* pegar el cuerpo copiado en B.3 (con el mismo `messageId` y el mismo `eventId`) y publicar **dos veces**.
3. **Esperado:** en `docker compose logs recepcion-presupuesto-api --tail 30` aparece que el evento está duplicado y se descarta, y en la base `DB_Recepcion` la OC sigue estando **una sola vez** (la tabla de eventos procesados tiene una sola fila con ese `EventId`).
4. Repetir lo mismo con `RequisicionAprobadaEvent` y Licitaciones (la requisición aprobada no se duplica).

### B.5 RabbitMQ caído (anotar lo que pasa, no hay un resultado "correcto" definido)
1. `docker compose stop rabbitmq`.
2. Aprobar una requisición. Anotar qué ve el usuario (¿error? ¿se aprueba igual pero no se publica el evento?).
3. `docker compose start rabbitmq` y anotar si el evento se pierde o llega. (Hasta donde se vio en la revisión del código, no hay un "Outbox" que guarde el evento si RabbitMQ no está: si se pierde, abrir una tarjeta.)

---

## Resultados

| Fecha | Prueba | Quién | Resultado (OK / Falló) | Observaciones / tarjeta de la falla |
|---|---|---|---|---|
| 2026-11-06 | A.1 Tests automáticos del Gateway | Agus | OK | 14 de 14 en verde (`dotnet test` con filtro `Resiliencia`). |
| 2026-11-06 | A.2 Servicio apagado: 502 con ProblemDetails | Agus | OK | 502 con `type`, `title`, `status`, `detail` y `correlationId`. También se vio en B.3 al apagar Recepción. |
| 2026-11-06 | A.2 Breaker abierto: respuesta inmediata | Agus | OK | Verificado en las pruebas automáticas. En Docker se vieron 502 seguidos con el servicio apagado, pero no se midió el tiempo de respuesta. |
| 2026-11-06 | A.2 Recuperación al volver el servicio | Agus | OK | Al volver el servicio, el Gateway respondió normal (404 del propio servicio). |
| 2026-11-06 | B.1 Colas con 1 consumidor | Agus | OK | `RequisicionAprobada` y `OrdenCompraEmitida`, 1 consumidor y 0 mensajes. |
| 2026-11-06 | B.2 Recorrido Requisición -> Licitación -> OC -> Recepción | Agus | OK (con 2 correcciones) | Script `scripts/flujo-e2e.ps1`. Hubo que corregir #132 (Docker de Licitaciones) y la tarjeta de las tablas de Orden de Compra (ver Hallazgos 1 y 2). La licitación hay que crearla por API: el evento no la crea sola. |
| 2026-11-06 | B.2 Sin cola `_error` con mensajes | Agus | OK | Solo existen las dos colas, ambas en 0 mensajes. |
| 2026-11-06 | B.3 Decimales como número en el mensaje | Agus | OK | Mensaje crudo en el panel: `"cantidad": 10.240`, `"precioUnitario": 1234.56`, `"montoTotal": 26784.53880`, sin comillas. |
| 2026-11-06 | B.3 Mensaje no se pierde con el consumidor caído | Agus | OK | Con Recepción apagada el mensaje esperó (0 consumidores, 1 mensaje) y se consumió al volver. |
| 2026-11-06 | B.4 Idempotencia en Recepción | Agus | OK | Mismo `EventId` dos veces: el segundo se descartó (log "duplicado ... se descarta"). |
| 2026-11-06 | B.4 Idempotencia en Licitaciones | Agus | No ejecutada | No hay endpoint de prueba equivalente al `TestController` de Recepción. Cubierto solo por tests del servicio. |
| 2026-11-06 | B.5 RabbitMQ caído (antes del arreglo #134) | Agus | Falló (hallazgo) | Aprobar tardaba 100 s y devolvía 504, la requisición quedaba `Aprobada` y el evento se perdía. |
| 2026-11-06 | B.5 RabbitMQ caído (con el outbox, #134) | Agus | OK | Aprobar responde en 0,5 s con RabbitMQ apagado; al volver RabbitMQ el evento se entrega solo y Licitaciones puede crear la licitación (`scripts/verificar-evento-outbox.ps1`). |

## Hallazgos de esta validación

1. **Licitaciones no construía en Docker (tarjeta #132).** El proyecto `Application` incluía archivos de `contracts/`, que quedan fuera del contexto de build de Docker. Se corrigió con una copia propia del evento `OrdenCompraEmitidaEvent` dentro del servicio.
2. **Adjudicar fallaba con una base real.** `OrdenCompra` y `OrdenCompraItem` existían como clases, pero no estaban registradas en `LicitacionesDbContext` ni tenían migración. Error: "Cannot create a DbSet for 'OrdenCompra'". Los tests no lo detectaron porque simulan el repositorio. Se corrigió registrando las entidades y agregando la migración `AgregarOrdenesCompra`.
3. **RabbitMQ caído al aprobar una requisición (resuelto en la tarjeta #134).** Antes, `POST /api/requisiciones/{id}/aprobar` quedaba esperando 100 s y el Gateway respondía 504; aun así la requisición quedaba `Aprobada` en `DB_Req`, el evento `RequisicionAprobada` nunca llegaba a Licitaciones y la requisición no podía reaprobarse. Se resolvió con el outbox transaccional de MassTransit con Entity Framework: el evento se guarda en `DB_Req` en la misma transacción que el cambio de estado y un servicio en segundo plano lo entrega a RabbitMQ. Resultado: aprobar responde en 0,5 s con RabbitMQ caído y el evento se entrega solo al volver. Costos: el servicio de Requisiciones pasó a EF Core 9 (lo exige `MassTransit.EntityFrameworkCore 8.5.12`), se agregaron 3 tablas (`InboxState`, `OutboxState`, `OutboxMessage`) y el outbox solo se activa con el transporte real (con el transporte en memoria de los tests se publica directo). **Pendiente:** Licitaciones guarda y después publica `OrdenCompraEmitida` en adjudicar, así que tiene el mismo riesgo y necesitaría el mismo arreglo.
4. **Observaciones menores en Licitaciones (para hablar con su squad):** el evento `OrdenCompraEmitida` sale con `Etapa` fija en `"Adjudicacion"` en vez de la etapa real de la requisición, y su `CorrelationId` es un GUID nuevo en lugar del de la request, lo que corta el rastreo entre servicios.
5. **Otras observaciones:** el Gateway no tiene CORS y Vite no tiene proxy (la PWA no puede usar el backend real desde el servidor de desarrollo); el `TestController` de Recepción es público y sin autenticación; con `MaxReintentos: 0` el Gateway no arranca (Polly exige al menos 1); las versiones de MassTransit difieren entre servicios.

## Notas para repetir la prueba en Windows PowerShell 5.1

- Enviar el cuerpo JSON como bytes UTF-8, o las tildes (por ejemplo "Mampostería") llegan mal y el servicio responde 400.
- El servicio rechaza con 409 requisiciones idénticas a una reciente (mismo solicitante, obra, etapa y cantidades): el script varía la cantidad en cada corrida.
- Scripts: `scripts/flujo-e2e.ps1` (recorrido completo), `scripts/rabbit-caido-e2e.ps1` (B.5, aprobar con RabbitMQ caído) y `scripts/verificar-evento-outbox.ps1` (comprueba que el evento llegó a Licitaciones).
