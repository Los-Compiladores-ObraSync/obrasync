import { errorDeFormatoNumerico, esUnidadEntera, MENSAJE_SOLO_ENTEROS } from '../formularios/numeros'

export const LARGO_MAXIMO_OBSERVACIONES = 1000

export interface ItemRequisicionForm {
  articuloId: string
  cantidad: string
  /** Unidad de medida del insumo elegido (BOLSA, KG…). Si es BOLSA, la cantidad tiene que ser entera. */
  unidadMedida?: string
}

export interface RequisicionForm {
  obraId: string
  etapa: string
  observaciones?: string
  items: ItemRequisicionForm[]
}

export interface ErroresItemRequisicion {
  articuloId?: string
  cantidad?: string
}

export interface ErroresRequisicion {
  obraId?: string
  etapa?: string
  observaciones?: string
  itemsGeneral?: string
  items?: Record<number, ErroresItemRequisicion>
}

/**
 * Valida los datos del formulario de requisición antes de enviarlo.
 * Reglas:
 * 1. Obra obligatoria.
 * 2. Etapa obligatoria en la UI.
 * 3. Al menos un ítem en la lista.
 * 4. Cada ítem debe tener un insumo seleccionado.
 * 5. Cantidad: número positivo mayor a 0. Los decimales llevan punto (nunca coma) y las bolsas van enteras.
 *    Vacío, 0 o negativos no son válidos.
 * 6. No se permite seleccionar el mismo insumo dos veces.
 */
export function validarRequisicion(form: RequisicionForm): ErroresRequisicion {
  const errores: ErroresRequisicion = {}

  if (!form.obraId || form.obraId.trim() === '') {
    errores.obraId = 'Tenés que seleccionar una obra.'
  }

  if (!form.etapa || form.etapa.trim() === '') {
    errores.etapa = 'Tenés que seleccionar una etapa de la obra.'
  }

  if ((form.observaciones?.trim().length ?? 0) > LARGO_MAXIMO_OBSERVACIONES) {
    errores.observaciones = `Las observaciones no pueden superar los ${LARGO_MAXIMO_OBSERVACIONES} caracteres.`
  }

  if (!form.items || form.items.length === 0) {
    errores.itemsGeneral = 'Tenés que agregar al menos un ítem a la requisición.'
    return errores
  }

  const itemsErrores: Record<number, ErroresItemRequisicion> = {}
  const articulosVistos = new Map<string, number>()

  form.items.forEach((item, index) => {
    const errorItem: ErroresItemRequisicion = {}

    if (!item.articuloId || item.articuloId.trim() === '') {
      errorItem.articuloId = 'Seleccioná un insumo del catálogo.'
    } else {
      if (articulosVistos.has(item.articuloId)) {
        errorItem.articuloId = 'Este insumo ya fue agregado en la lista.'
      } else {
        articulosVistos.set(item.articuloId, index)
      }
    }

    const cantidadStr = item.cantidad ? item.cantidad.trim() : ''
    if (cantidadStr === '') {
      errorItem.cantidad = 'Ingresá una cantidad.'
    } else {
      const entera = esUnidadEntera(item.unidadMedida)
      const errorFormato = errorDeFormatoNumerico(cantidadStr, entera)
      const cantidadNum = Number(cantidadStr)
      if (errorFormato) {
        errorItem.cantidad = errorFormato
      } else if (Number.isNaN(cantidadNum) || cantidadNum <= 0) {
        errorItem.cantidad = 'La cantidad debe ser mayor a 0.'
      } else if (entera && !Number.isInteger(cantidadNum)) {
        errorItem.cantidad = MENSAJE_SOLO_ENTEROS
      }
    }

    if (errorItem.articuloId || errorItem.cantidad) {
      itemsErrores[index] = errorItem
    }
  })

  if (Object.keys(itemsErrores).length > 0) {
    errores.items = itemsErrores
  }

  return errores
}

/**
 * Determina si el objeto de errores tiene al menos una falla de validación.
 */
export function hayErrores(errores: ErroresRequisicion): boolean {
  if (errores.obraId || errores.etapa || errores.observaciones || errores.itemsGeneral) {
    return true
  }
  if (errores.items && Object.keys(errores.items).length > 0) {
    return true
  }
  return false
}

/**
 * Mapea los estados según requerimiento de la UI:
 * - 'Pendiente' -> 'Aprobación Pendiente'
 * - 'Aprobada' -> 'Aprobada'
 * - 'Rechazada' -> 'Rechazada'
 * - 'Cancelada' -> 'Cancelada'
 */
export function mapearEstadoRequisicion(estado: string): string {
  switch (estado) {
    case 'Pendiente':
      return 'Aprobación Pendiente'
    case 'Aprobada':
      return 'Aprobada'
    case 'Rechazada':
      return 'Rechazada'
    default:
      return estado
  }
}
