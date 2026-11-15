import { ApiError } from '../api/ApiError'

/** Texto en español para un error al confirmar una acción del asistente (409, 403, 400, sesión vencida, red caída). */
export function mensajeDeErrorAlConfirmar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) {
      return err.detail ?? 'Hay datos inválidos. Revisá el borrador.'
    }
    if (err.status === 401) {
      return 'Tu sesión venció. Volvé a iniciar sesión.'
    }
    if (err.status === 403) {
      return 'No tenés permiso para hacer esto.'
    }
    if (err.status === 404) {
      return err.detail ?? 'Algo del borrador ya no existe. Pedile al asistente que lo arme de nuevo.'
    }
    if (err.status === 409) {
      return (
        err.detail ??
        'Ya hay algo igual cargado hace un momento. Revisá el historial antes de volver a intentar.'
      )
    }
    return err.detail ?? 'El servidor no pudo completar la acción. Probá de nuevo.'
  }
  return 'No se pudo conectar con el servidor. Probá de nuevo.'
}
