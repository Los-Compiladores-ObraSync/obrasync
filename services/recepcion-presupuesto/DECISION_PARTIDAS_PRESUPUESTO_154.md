# Decisión de Diseño: Ciclo de Control Presupuestario y Eventos (#154)

**Squad:** Recepción y Control Presupuestario  
**Sprint:** 3  
**Tarjeta Trello:** #154 (`https://trello.com/c/Uuvd5KPq`)  
**Fecha:** 15 de Noviembre de 2026  
**Alineación con:** Tarjetas #90, #91, #92, #93, #94, #95 y #96

---

## 1. Contexto y Problema Detectado

En la revisión de arquitectura del 13/11 se identificaron dos inconsistencias en el microservicio de Recepción:
1. **Columnas no actualizadas en base de datos:** En la entidad `PartidaPresupuestaria`, las columnas `MontoComprometido` y `MontoEjecutado` estaban definidas y mapeadas en SQL Server, pero ningún caso de uso ni consumidor las actualizaba. Si nadie las actualiza, la regla de desvío presupuestario (#94) siempre compararía contra 0 y nunca detectaría desvíos.
2. **Evento publicado sin consumidor:** Al confirmar una recepción, se publica `RecepcionConfirmadaEvent` a RabbitMQ, pero ningún servicio del sistema lo consume.

Esta decisión formaliza el ciclo contable del microservicio y define las responsabilidades de cada componente para destrabar la implementación de las tarjetas **#93**, **#94**, **#95** y **#96**.

---

## 2. Decisiones de Arquitectura y Negocio

### 2.1 Ciclo de Vida de los Montos de la Partida

El control presupuestario opera en 3 estados contables:

$$\text{Monto Asignado} \xrightarrow[\text{al emitir OC}]{\text{Compromiso}} \text{Monto Comprometido} \xrightarrow[\text{al recepcionar}]{\text{Ejecución}} \text{Monto Ejecutado}$$

1. **`MontoAsignado` (Presupuesto fijado):**
   * Se define al dar de alta la partida por Obra y Etapa (`POST /api/presupuestos/partidas`, tarjeta #93).
   * Solo los roles con permisos de gestión presupuestaria (`Admin`, `Comprador`) pueden crearlo o ajustarlo.

2. **`MontoComprometido` (Reserva preventiva por Orden de Compra):**
   * **Cuándo sube:** Al recibir `OrdenCompraEmitidaEvent` en el consumidor `OrdenCompraEmitidaConsumer`.
   * **Condición de bloqueo (#95):** Se evalúa la regla de desvío presupuestario contra la partida de la obra/etapa:
     $$\text{MontoComprometido} + \text{MontoOC} > \text{MontoAsignado}$$
     * **Si NO supera:** La orden queda activa para recepción y se incrementa inmediatamente `MontoComprometido += MontoOC`.
     * **Si SUPERA la partida:** La orden se marca en estado `Bloqueada` (desvío presupuestario). **NO** se incrementa `MontoComprometido` preventivamente y **NO** se muestra en `ordenes-pendientes` para evitar que el Capataz reciba mercadería no autorizada.
     * **Desbloqueo (#96):** Cuando un `Admin` autoriza explícitamente la orden bloqueada (`POST /api/recepciones/ordenes/{id}/liberar`), la orden pasa a activa y en ese instante se suma su monto al `MontoComprometido`.

3. **`MontoEjecutado` (Gasto real devengado por entrega física):**
   * **Cuándo sube:** En `CrearRecepcionCommandHandler`, cuando el Capataz registra la llegada de mercadería.
   * **Monto computable:** Se calcula multiplicando la cantidad efectivamente recibida por el precio unitario pactado en la OC:
     $$\text{MontoRecibido} = \sum (\text{item.CantidadRecibida} \times \text{item.PrecioUnitario})$$
   * Se incrementa `MontoEjecutado += MontoRecibido`.

---

### 2.2 Tratamiento de Entregas Parciales y Remanentes (#90, #91, #92)

* Cuando una recepción es parcial (`CantidadRecibida < CantidadEsperada`):
  * La porción recibida suma a `MontoEjecutado`.
  * La porción restante (remanente pendiente de entrega) **permanece en `MontoComprometido`**, garantizando que el presupuesto siga reservado para cuando el proveedor entregue el saldo faltante.
  * Si la orden fuera cancelada administrativamente con saldo pendiente, el saldo no entregado se descuenta de `MontoComprometido`.
  * Mientras exista remanente pendiente, se bloquea la liquidación administrativa del excedente no entregado (#92).

---

### 2.3 Regla de Desvío Presupuestario (#94)

* **Definición de "Gasto Acumulado":** Es el valor actual de `MontoComprometido`.
* **Cálculo de Desvío:**
  ```csharp
  bool hayDesvio = (partida.MontoComprometido + montoNuevaOc) > partida.MontoAsignado;
  ```
* **Casos particulares:**
  * **Igualdad estricta:** Si `MontoComprometido + MontoOC == MontoAsignado`, **NO** hay desvío (la regla es mayor estricto `>`).
  * **Sin partida definida:** Si una obra o etapa no tiene partida presupuestaria cargada, no se bloquea preventivamente la operación (se asume obra sin tope presupuestario cargado).
  * **Tipos de datos:** Todos los cálculos monetarios se realizan con tipo `decimal` (nunca `double` ni `float`).

---

### 2.4 Destino y Consumo de `RecepcionConfirmadaEvent`

* `RecepcionConfirmadaEvent` se ratifica formalmente como un **evento de integración informativo, de auditoría y notificación**:
  * Es consumido por el módulo de notificaciones y campana del usuario (#163) y puesto a disposición de las herramientas de lectura del asistente de IA (#147) para informar al Capataz y a Compras.
  * **No** se crea un consumidor backend acoplado en otros servicios que muten base de datos (por ejemplo, cambiar forzosamente el estado de una licitación o requisición), preservando el principio de bajo acoplamiento entre bounded contexts.

---

### 2.5 Idempotencia en Mensajería

* `OrdenCompraEmitidaConsumer` utiliza el `OrdenCompraId` para asegurar que el reenvío de un mismo evento por parte de RabbitMQ no duplique el registro de la orden ni aplique dos veces el incremento a `MontoComprometido`.

---

## 3. Impacto en Tarjetas del Sprint 3

| Tarjeta | Responsable | Impacto de esta decisión |
| :--- | :--- | :--- |
| **#93** | Miembro 3 | Agrega endpoints `GET` y `POST /api/presupuestos/partidas` para dar de alta y consultar las partidas con sus 3 montos. |
| **#94** | Miembro 3 | Implementa la función pura `(MontoComprometido + MontoOC) > MontoAsignado` con pruebas unitarias de casos borde. |
| **#95** | Cami | Al consumir `OrdenCompraEmitidaEvent`, invoca la regla de #94. Si hay desvío, marca la OC en estado `Bloqueada` y no suma a comprometido. |
| **#96** | Cami | Endpoint `POST /api/recepciones/ordenes/{id}/liberar` para rol `Admin`; al liberar suma el monto a `MontoComprometido`. |
| **#90 / #91** | Ramiro | Entregas parciales actualizan `MontoEjecutado` por lo recibido y mantienen el remanente en `MontoComprometido`. |
| **#92** | Joaquín | Bloquea liquidación de saldo no recepcionado. |
