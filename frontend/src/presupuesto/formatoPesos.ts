const formateadorPesos = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
})

/**
 * Formatea un monto numérico como importe en pesos argentinos (ARS, es-AR).
 * Ejemplo: 1250000 -> "$ 1.250.000,00"
 */
export function formatearPesos(monto: number): string {
  return formateadorPesos.format(monto)
}
