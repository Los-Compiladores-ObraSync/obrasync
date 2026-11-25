import { parsearPrecioDeArchivo } from '../presupuesto/montoTexto'
import type { CrearInsumoRequest } from './catalogoTipos'

/**
 * Lectura y validación del CSV de insumos (#215). Todo corre en el navegador: no se agrega ninguna
 * librería (decisión de la #116) y el servidor sigue siendo quien valida de verdad cada fila.
 * Acá solo se detectan los errores de formato antes de enviar, para no gastar pedidos de más.
 */

/** Tope de filas por archivo: se envían una por una, así que un archivo enorme tardaría demasiado. */
export const MAX_FILAS = 500
export const MAX_BYTES = 1_000_000

/**
 * Plantilla descargable: SOLO los encabezados, sin filas de ejemplo. Si trajera ejemplos, quien la suba sin
 * borrarlos cargaría insumos falsos en el catálogo real. Usa ";" como separador porque es lo que Excel en
 * español guarda como CSV (la coma ya se usa para los decimales: 8500,50). El lector acepta "," o ";".
 */
// Los encabezados con tildes y espacios se ven mejor en Excel; el lector los reconoce igual (ver normalizarEncabezado).
export const PLANTILLA_CSV = 'Código;Descripción;Unidad de medida;Moneda;Precio de referencia;Activo\r\n'

export interface InsumoParaCargar {
  /** Número de fila en el archivo (el encabezado es la fila 1, igual que en la planilla). */
  fila: number
  datos: CrearInsumoRequest
}

export interface ErrorDeFila {
  fila: number
  motivo: string
}

export interface ResultadoValidacion {
  insumos: InsumoParaCargar[]
  errores: ErrorDeFila[]
  /** Problema de todo el archivo (columnas que faltan, vacío, demasiado grande). Si existe, no se carga nada. */
  errorGeneral: string | null
}

export function detectarSeparador(texto: string): ',' | ';' {
  const primeraLinea = texto.split(/\r?\n/, 1)[0] ?? ''
  const puntoYComa = (primeraLinea.match(/;/g) ?? []).length
  const comas = (primeraLinea.match(/,/g) ?? []).length
  return puntoYComa > comas ? ';' : ','
}

/** Parte el texto en filas y columnas. Respeta las comillas ("a;b" es un solo campo) y los saltos CRLF. */
export function parsearCsv(texto: string, separador: ',' | ';' = detectarSeparador(texto)): string[][] {
  const filas: string[][] = []
  let fila: string[] = []
  let campo = ''
  let entreComillas = false

  const cerrarCampo = () => {
    fila.push(campo)
    campo = ''
  }
  const cerrarFila = () => {
    cerrarCampo()
    filas.push(fila)
    fila = []
  }

  for (let i = 0; i < texto.length; i += 1) {
    const caracter = texto[i]
    if (entreComillas) {
      if (caracter === '"') {
        if (texto[i + 1] === '"') {
          campo += '"'
          i += 1
        } else {
          entreComillas = false
        }
      } else {
        campo += caracter
      }
    } else if (caracter === '"') {
      entreComillas = true
    } else if (caracter === separador) {
      cerrarCampo()
    } else if (caracter === '\r') {
      if (texto[i + 1] === '\n') i += 1
      cerrarFila()
    } else if (caracter === '\n') {
      cerrarFila()
    } else {
      campo += caracter
    }
  }
  if (campo !== '' || fila.length > 0) cerrarFila()
  return filas
}

type Columna = 'codigo' | 'descripcion' | 'unidadMedida' | 'moneda' | 'precioReferencia' | 'activo'

// El encabezado se compara sin mayúsculas, tildes ni símbolos: "Unidad de medida", "unidadMedida" y
// "UNIDAD_MEDIDA" son la misma columna.
const COLUMNAS: Record<string, Columna> = {
  codigo: 'codigo',
  descripcion: 'descripcion',
  unidadmedida: 'unidadMedida',
  unidaddemedida: 'unidadMedida',
  moneda: 'moneda',
  precioreferencia: 'precioReferencia',
  preciodereferencia: 'precioReferencia',
  activo: 'activo',
}

const OBLIGATORIAS: readonly Columna[] = ['codigo', 'descripcion', 'unidadMedida']

// Cómo se llama cada columna obligatoria en la plantilla (es lo que lee la persona en el mensaje de error).
const ETIQUETAS: Record<Columna, string> = {
  codigo: 'Código',
  descripcion: 'Descripción',
  unidadMedida: 'Unidad de medida',
  moneda: 'Moneda',
  precioReferencia: 'Precio de referencia',
  activo: 'Activo',
}

function normalizarEncabezado(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '')
}

/** Interpreta "si/no", "true/false", "1/0". Vacío = no se manda (el servidor usa activo). null = no se entiende. */
function leerActivo(texto: string): boolean | undefined | null {
  if (texto === '') return undefined
  const valor = normalizarEncabezado(texto)
  if (['si', 'true', '1', 'verdadero'].includes(valor)) return true
  if (['no', 'false', '0', 'falso'].includes(valor)) return false
  return null
}

function estaVacia(fila: string[]): boolean {
  return fila.every((celda) => celda.trim() === '')
}

export function validarCsv(textoOriginal: string): ResultadoValidacion {
  const vacio = (errorGeneral: string): ResultadoValidacion => ({ insumos: [], errores: [], errorGeneral })

  const texto = textoOriginal.replace(/^\uFEFF/, '')
  const filas = parsearCsv(texto)
  if (filas.length === 0 || estaVacia(filas[0])) return vacio('El archivo está vacío.')

  const posicion = new Map<Columna, number>()
  filas[0].forEach((encabezado, indice) => {
    const columna = COLUMNAS[normalizarEncabezado(encabezado)]
    if (columna && !posicion.has(columna)) posicion.set(columna, indice)
  })

  const faltantes = OBLIGATORIAS.filter((columna) => !posicion.has(columna))
  if (faltantes.length > 0) {
    return vacio(
      `Faltan columnas obligatorias: ${faltantes.map((columna) => ETIQUETAS[columna]).join(', ')}. Descargá la plantilla y usá esos encabezados.`,
    )
  }

  const filasConDatos = filas.slice(1).filter((fila) => !estaVacia(fila)).length
  if (filasConDatos === 0) return vacio('El archivo no tiene filas con insumos.')
  if (filasConDatos > MAX_FILAS) {
    return vacio(`El archivo tiene más de ${MAX_FILAS} filas. Dividilo en archivos más chicos.`)
  }

  const insumos: InsumoParaCargar[] = []
  const errores: ErrorDeFila[] = []
  const primeraFilaDelCodigo = new Map<string, number>()

  filas.slice(1).forEach((valores, indice) => {
    if (estaVacia(valores)) return
    const fila = indice + 2
    const celda = (columna: Columna): string => {
      const posicionColumna = posicion.get(columna)
      return posicionColumna === undefined ? '' : (valores[posicionColumna] ?? '').trim()
    }

    const motivos: string[] = []
    const codigo = celda('codigo')
    const descripcion = celda('descripcion')
    const unidadMedida = celda('unidadMedida')
    if (codigo === '') motivos.push('Falta el código.')
    if (descripcion === '') motivos.push('Falta la descripción.')
    if (unidadMedida === '') motivos.push('Falta la unidad de medida.')

    const textoPrecio = celda('precioReferencia')
    let precioReferencia: number | undefined
    if (textoPrecio !== '') {
      const precio = parsearPrecioDeArchivo(textoPrecio)
      if (precio === null) motivos.push('El precio no es válido (usá números, por ejemplo 8500,50).')
      else precioReferencia = precio
    }

    const activo = leerActivo(celda('activo'))
    if (activo === null) motivos.push('La columna "activo" debe decir si o no.')

    if (codigo !== '') {
      const clave = codigo.toLowerCase()
      const anterior = primeraFilaDelCodigo.get(clave)
      if (anterior !== undefined) motivos.push(`El código ${codigo} está repetido (ya aparece en la fila ${anterior}).`)
      else primeraFilaDelCodigo.set(clave, fila)
    }

    if (motivos.length > 0) {
      errores.push({ fila, motivo: motivos.join(' ') })
      return
    }

    const datos: CrearInsumoRequest = { codigo, descripcion, unidadMedida }
    const moneda = celda('moneda')
    if (moneda !== '') datos.moneda = moneda.toUpperCase()
    if (precioReferencia !== undefined) datos.precioReferencia = precioReferencia
    if (activo !== undefined && activo !== null) datos.activo = activo
    insumos.push({ fila, datos })
  })

  return { insumos, errores, errorGeneral: null }
}
