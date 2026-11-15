/**
 * Paginación de los listados (tarjeta #172).
 *
 * Los servicios aceptan ?pagina=N&tamano=M (opcionales) y publican el total en el encabezado
 * X-Total-Count. El cuerpo sigue siendo la lista de siempre. Mientras un servicio todavía no
 * publique ese encabezado, "total" queda sin definir y la pantalla se comporta como antes.
 */

/** Filas que se piden por página. Es el máximo provisorio acordado en #172: lo confirma el equipo. */
export const TAMANO_PAGINA = 50

/** Tope de seguridad de traerTodas: 20 páginas de 50 = 1000 filas. Evita un bucle infinito si un
 *  servicio devolviera un total que nunca se alcanza. */
const MAX_PAGINAS_TRAER_TODAS = 20

export interface PaginaDe<T> {
  items: T[]
  /** Total de filas que cumplen los filtros (X-Total-Count). Sin definir si el servicio no lo envía. */
  total?: number
}

/** Agrega ?pagina=&tamano= a una ruta (respeta si ya tiene otros parámetros). */
export function conPaginacion(
  ruta: string,
  pagina: number,
  tamano: number = TAMANO_PAGINA,
): string {
  const separador = ruta.includes('?') ? '&' : '?'
  return `${ruta}${separador}pagina=${pagina}&tamano=${tamano}`
}

/** ¿Faltan filas por traer? Sin total conocido se asume que no (como antes de la paginación). */
export function hayMas(cargadas: number, total: number | undefined): boolean {
  return total !== undefined && cargadas < total
}

/**
 * Trae todas las páginas de un listado. Se usa donde la pantalla necesita la lista completa
 * (nombres de obras, selectores) y no tiene sentido un "Ver más".
 */
export async function traerTodas<T>(
  pedirPagina: (pagina: number) => Promise<PaginaDe<T>>,
): Promise<T[]> {
  const todas: T[] = []
  for (let pagina = 1; pagina <= MAX_PAGINAS_TRAER_TODAS; pagina++) {
    const { items, total } = await pedirPagina(pagina)
    todas.push(...items)
    if (items.length === 0 || !hayMas(todas.length, total)) break
  }
  return todas
}
