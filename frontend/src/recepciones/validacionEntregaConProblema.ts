// Reglas del formulario "Hubo un problema con la entrega" (#224). Son las del contrato de
// POST /api/recepciones/problemas: motivo obligatorio (uno de cuatro), detalle de hasta 500 caracteres y
// hasta 3 fotos. El servicio valida igual; esto solo evita ir al servidor con datos obviamente mal.

import { MAX_FOTOS_EVIDENCIA } from './imagenEvidencia'
import { MOTIVOS_PROBLEMA, type MotivoProblema } from './recepcionesTipos'

export const LARGO_MAXIMO_DETALLE_PROBLEMA = 500

export const MOTIVO_INCOMPLETA: MotivoProblema = 'Llegó incompleta'

/** Cuando llegó incompleta: ¿todo o algo específico? '' si todavía no eligió. */
export type AlcanceIncompleta = '' | 'todo' | 'algo'

export interface DatosProblemaForm {
  /** '' si todavía no eligió ninguno. */
  motivo: MotivoProblema | ''
  detalle: string
  cantidadFotos: number
  /** Solo cuenta si el motivo es "Llegó incompleta". */
  alcanceIncompleta?: AlcanceIncompleta
  /** Descripciones de los materiales marcados como incompletos (alcance "algo"). */
  materialesIncompletos?: string[]
}

export interface ErroresProblema {
  motivo?: string
  alcance?: string
  detalle?: string
  fotos?: string
}

/**
 * Arma el detalle que viaja al servidor. El contrato solo tiene un texto libre de hasta 500 caracteres, así que
 * los materiales marcados como incompletos se escriben al principio del mismo texto (sin cambiar el contrato).
 */
export function armarDetalleProblema(datos: DatosProblemaForm): string {
  const libre = datos.detalle.trim()
  let prefijo = ''
  if (datos.motivo === MOTIVO_INCOMPLETA) {
    if (datos.alcanceIncompleta === 'todo') prefijo = 'Llegó incompleto todo el pedido.'
    else if (datos.alcanceIncompleta === 'algo' && (datos.materialesIncompletos?.length ?? 0) > 0) {
      prefijo = `Llegó incompleto: ${(datos.materialesIncompletos ?? []).join(', ')}.`
    }
  }
  return [prefijo, libre].filter(Boolean).join(' ')
}

export function esMotivoProblema(valor: string): valor is MotivoProblema {
  return (MOTIVOS_PROBLEMA as readonly string[]).includes(valor)
}

export function validarEntregaConProblema(datos: DatosProblemaForm): ErroresProblema {
  const errores: ErroresProblema = {}

  if (!datos.motivo) {
    errores.motivo = 'Elegí qué pasó con la entrega.'
  } else if (!esMotivoProblema(datos.motivo)) {
    errores.motivo = 'Elegí uno de los motivos de la lista.'
  }

  if (datos.motivo === MOTIVO_INCOMPLETA) {
    if (!datos.alcanceIncompleta) {
      errores.alcance = 'Contanos si llegó incompleto todo o solo algo.'
    } else if (datos.alcanceIncompleta === 'algo' && (datos.materialesIncompletos?.length ?? 0) === 0) {
      errores.alcance = 'Marcá qué materiales llegaron incompletos.'
    }
  }

  if (datos.detalle.trim().length > LARGO_MAXIMO_DETALLE_PROBLEMA) {
    errores.detalle = `El detalle no puede tener más de ${LARGO_MAXIMO_DETALLE_PROBLEMA} caracteres.`
  } else if (armarDetalleProblema(datos).length > LARGO_MAXIMO_DETALLE_PROBLEMA) {
    errores.detalle = `Entre los materiales marcados y tu texto se pasa de ${LARGO_MAXIMO_DETALLE_PROBLEMA} caracteres: acortá el detalle.`
  }

  if (datos.cantidadFotos > MAX_FOTOS_EVIDENCIA) {
    errores.fotos = `Podés adjuntar hasta ${MAX_FOTOS_EVIDENCIA} fotos.`
  }

  return errores
}

export function hayErroresProblema(errores: ErroresProblema): boolean {
  return Object.values(errores).some((error) => error !== undefined)
}
