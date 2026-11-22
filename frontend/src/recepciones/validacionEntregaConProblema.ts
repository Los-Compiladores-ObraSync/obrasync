// Reglas del formulario "Hubo un problema con la entrega" (#224). Son las del contrato de
// POST /api/recepciones/problemas: motivo obligatorio (uno de cuatro), detalle de hasta 500 caracteres y
// hasta 3 fotos. El servicio valida igual; esto solo evita ir al servidor con datos obviamente mal.

import { MAX_FOTOS_EVIDENCIA } from './imagenEvidencia'
import { MOTIVOS_PROBLEMA, type MotivoProblema } from './recepcionesTipos'

export const LARGO_MAXIMO_DETALLE_PROBLEMA = 500

export interface DatosProblemaForm {
  /** '' si todavía no eligió ninguno. */
  motivo: MotivoProblema | ''
  detalle: string
  cantidadFotos: number
}

export interface ErroresProblema {
  motivo?: string
  detalle?: string
  fotos?: string
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

  if (datos.detalle.trim().length > LARGO_MAXIMO_DETALLE_PROBLEMA) {
    errores.detalle = `El detalle no puede tener más de ${LARGO_MAXIMO_DETALLE_PROBLEMA} caracteres.`
  }

  if (datos.cantidadFotos > MAX_FOTOS_EVIDENCIA) {
    errores.fotos = `Podés adjuntar hasta ${MAX_FOTOS_EVIDENCIA} fotos.`
  }

  return errores
}

export function hayErroresProblema(errores: ErroresProblema): boolean {
  return Object.values(errores).some((error) => error !== undefined)
}
