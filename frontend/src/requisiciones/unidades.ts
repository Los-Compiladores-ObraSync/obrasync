// El catálogo guarda las unidades como códigos cortos (BOLSA, M3...). En pantalla se muestran
// con su nombre completo, para que se entiendan de un vistazo (sobre todo "M3").
// Si aparece un código que no está en la tabla, se muestra tal cual vino del catálogo.

interface NombreUnidad {
  singular: string
  plural: string
}

const UNIDADES: Record<string, NombreUnidad> = {
  BOLSA: { singular: 'bolsa', plural: 'bolsas' },
  KG: { singular: 'kilo', plural: 'kilos' },
  M3: { singular: 'metro cúbico (m³)', plural: 'metros cúbicos (m³)' },
  UNIDAD: { singular: 'unidad', plural: 'unidades' },
}

/** Nombre de la unidad para mostrar solo, con mayúscula inicial: "Bolsas", "Metros cúbicos (m³)". */
export function etiquetaUnidad(codigo: string): string {
  const nombre = UNIDADES[codigo]
  if (!nombre) return codigo
  return nombre.plural.charAt(0).toUpperCase() + nombre.plural.slice(1)
}

/** Cantidad con su unidad, en singular o plural: "1 bolsa", "80 bolsas", "5 metros cúbicos (m³)". */
export function cantidadConUnidad(cantidad: number, codigo: string): string {
  const nombre = UNIDADES[codigo]
  if (!nombre) return `${cantidad} ${codigo}`
  return `${cantidad} ${cantidad === 1 ? nombre.singular : nombre.plural}`
}
