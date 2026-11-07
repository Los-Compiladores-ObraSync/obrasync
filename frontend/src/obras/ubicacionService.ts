// Validación de la ubicación de una obra contra OpenStreetMap (Nominatim), tarjeta #113.
// Decisión del equipo (#112): solo se comprueba que la dirección exista. No hay autocompletado y
// no se guardan coordenadas: Obra.Ubicacion sigue siendo un texto.
//
// Política de uso de Nominatim (https://operations.osmfoundation.org/policies/nominatim/),
// revisar si cambia: máximo 1 pedido por segundo para toda la aplicación, solo búsquedas que
// dispara el usuario (nada de autocompletar mientras escribe), identificar la aplicación
// (el navegador manda el Referer solo) y cachear los resultados.

export type ResultadoUbicacion = 'encontrada' | 'no-encontrada' | 'no-disponible'

const URL_NOMINATIM = 'https://nominatim.openstreetmap.org/search'
/** Si el servicio tarda más que esto se da por no disponible: un servicio externo no frena el flujo. */
const TIMEOUT_MS = 5000
/** Separación mínima entre dos pedidos distintos (política: 1 por segundo). */
export const INTERVALO_MINIMO_MS = 1000

// Resultados ya conocidos. Los "no disponible" NO se guardan: si el servicio vuelve, se reintenta.
const cache = new Map<string, ResultadoUbicacion>()
// Pedidos en vuelo: si el usuario sale del campo y envía el formulario casi a la vez, es UN solo pedido.
const enCurso = new Map<string, Promise<ResultadoUbicacion>>()
let ultimoPedido = 0

function normalizar(direccion: string): string {
  return direccion.trim().toLowerCase().replace(/\s+/g, ' ')
}

/** Solo para tests: vuelve la caché y el reloj de pedidos a cero. */
export function reiniciarUbicacionServiceParaTests(): void {
  cache.clear()
  enCurso.clear()
  ultimoPedido = 0
}

async function esperarTurno(): Promise<void> {
  const espera = ultimoPedido + INTERVALO_MINIMO_MS - Date.now()
  // Se reserva el turno ya, así dos pedidos seguidos no esperan lo mismo y salen juntos.
  ultimoPedido = Math.max(Date.now(), ultimoPedido + INTERVALO_MINIMO_MS)
  if (espera > 0) await new Promise((resolver) => setTimeout(resolver, espera))
}

async function consultar(direccion: string): Promise<ResultadoUbicacion> {
  await esperarTurno()
  const control = new AbortController()
  const temporizador = setTimeout(() => control.abort(), TIMEOUT_MS)
  try {
    const params = new URLSearchParams({
      q: direccion.trim(),
      format: 'jsonv2',
      limit: '1',
      'accept-language': 'es',
    })
    // OJO: fetch común, NO apiFetch. apiFetch agrega el token JWT y el X-Correlation-ID, que no
    // deben salir hacia un servicio de terceros (y el segundo exigiría un preflight de CORS).
    const respuesta = await fetch(`${URL_NOMINATIM}?${params.toString()}`, {
      signal: control.signal,
      headers: { Accept: 'application/json' },
    })
    if (!respuesta.ok) return 'no-disponible' // 429 (límite), 403 (bloqueo), 5xx…
    const resultados: unknown = await respuesta.json()
    if (!Array.isArray(resultados)) return 'no-disponible'
    return resultados.length > 0 ? 'encontrada' : 'no-encontrada'
  } catch {
    return 'no-disponible' // sin red, tiempo agotado o respuesta ilegible
  } finally {
    clearTimeout(temporizador)
  }
}

/**
 * Comprueba si una dirección existe. Nunca lanza error: si el servicio falla devuelve
 * 'no-disponible' y la pantalla deja crear la obra igual.
 */
export function validarUbicacion(direccion: string): Promise<ResultadoUbicacion> {
  const clave = normalizar(direccion)
  const guardado = cache.get(clave)
  if (guardado) return Promise.resolve(guardado)

  const pendiente = enCurso.get(clave)
  if (pendiente) return pendiente

  const pedido = consultar(direccion)
    .then((resultado) => {
      if (resultado !== 'no-disponible') cache.set(clave, resultado)
      return resultado
    })
    .finally(() => enCurso.delete(clave))
  enCurso.set(clave, pedido)
  return pedido
}
