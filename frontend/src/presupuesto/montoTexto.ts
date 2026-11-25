/**
 * Convierte el monto escrito por la persona a número (#161): pesos ENTEROS, sin coma ni decimales.
 * Acepta el punto solo como separador de miles ("1.500.000" o "1500000"). Nunca coma: "1500,50" no es válido.
 * Devuelve null si no es un monto válido: vacío, negativo, con coma, con decimales o letras.
 *
 * Por qué se parsea a mano y no con parseFloat: parseFloat("1.500") daría 1,5 en vez de 1500,
 * y en un presupuesto ese error de mil veces no se vería hasta que una orden se bloquee de más.
 */
export function parsearMonto(texto: string): number | null {
  const limpio = texto.trim().replace(/\s/g, '').replace(/^\$/, '')
  if (limpio === '') return null
  const conMiles = /^\d{1,3}(\.\d{3})+$/
  const sinMiles = /^\d+$/
  if (!conMiles.test(limpio) && !sinMiles.test(limpio)) return null
  const numero = Number(limpio.replace(/\./g, ''))
  // decimal(18,2) en la base: más de 16 dígitos enteros no entra
  return Number.isFinite(numero) && numero >= 0 && numero < 1e16 ? numero : null
}

/**
 * Precio que viene de un ARCHIVO CSV (carga masiva de insumos): ahí sí puede llegar en formato argentino,
 * con punto de miles y coma decimal ("1.500.000,50"), porque así lo exporta Excel. No es un campo que alguien escribe.
 * Devuelve null si no es un número válido: vacío, negativo, letras o más de 2 decimales.
 */
export function parsearPrecioDeArchivo(texto: string): number | null {
  const limpio = texto.trim().replace(/\s/g, '').replace(/^\$/, '')
  if (limpio === '') return null
  const conMiles = /^\d{1,3}(\.\d{3})+(,\d{1,2})?$/
  const sinMiles = /^\d+(,\d{1,2})?$/
  if (!conMiles.test(limpio) && !sinMiles.test(limpio)) return null
  const numero = Number(limpio.replace(/\./g, '').replace(',', '.'))
  return Number.isFinite(numero) && numero >= 0 && numero < 1e16 ? numero : null
}
