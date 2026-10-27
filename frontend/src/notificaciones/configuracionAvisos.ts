/** Cada cuánto se vuelve a preguntar cuántas requisiciones esperan aprobación.
 *  Valor propuesto en #48 (60 segundos). Está en un solo lugar para cambiarlo fácil. */
export const INTERVALO_REFRESCO_MS = 60_000

/** Roles que pueden aprobar requisiciones (POST /aprobar), y por eso son los
 *  únicos que ven el número en la campana. */
export const ROLES_QUE_VEN_PENDIENTES: readonly string[] = ['Comprador', 'Admin']
