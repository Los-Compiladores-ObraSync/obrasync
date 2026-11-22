/** Mientras el chat no tiene conexión en vivo, se vuelve a consultar cada tanto (#225: 10 segundos). */
export const INTERVALO_RESPALDO_MS = 10_000

/** Si la conexión en vivo no se pudo abrir (o se cortó del todo), se vuelve a intentar cada tanto. */
export const REINTENTO_CONEXION_MS = 15_000

/** Cada cuántos ciclos de consulta de respaldo se vuelve a preguntar también por el problema abierto de la
 *  orden (no hay un endpoint solo para eso: se lee de la lista de órdenes, que es más pesada). */
export const CICLOS_ENTRE_CONSULTAS_DE_PROBLEMA = 3

/** Largo máximo de un mensaje (contrato: EnviarMensajeChatRequestDto, 1 a 1000 caracteres). */
export const MAX_MENSAJE = 1000

/** Cada cuánto se vuelve a preguntar cuántos mensajes sin leer tiene cada orden en las listas de órdenes. */
export const INTERVALO_NO_LEIDOS_MS = 30_000
