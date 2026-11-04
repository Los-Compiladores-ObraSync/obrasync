// Reglas del formulario de recepción. Las del contrato (largos máximos) y la decisión de #127:
// la fecha de recepción NO puede ser futura. El backend valida igual (acepta 1 día de tolerancia
// por la zona horaria); esto solo evita ir al servidor con datos obviamente mal.

import { esFechaFutura, fechaEnPalabras } from '../obras/fechaEnPalabras'

export const LARGO_MAXIMO_REMITO = 50
export const LARGO_MAXIMO_OBSERVACIONES = 500

export interface DatosRecepcionForm {
  numeroRemito: string
  /** Formato que da <input type="date">: AAAA-MM-DD, o '' si está vacío. */
  fecha: string
  observaciones: string
}

export interface ErroresRecepcion {
  numeroRemito?: string
  fecha?: string
  observaciones?: string
}

export function validarRecepcion(
  datos: DatosRecepcionForm,
  ahora: Date = new Date(),
): ErroresRecepcion {
  const errores: ErroresRecepcion = {}

  const remito = datos.numeroRemito.trim()
  if (!remito) {
    errores.numeroRemito = 'El número de remito es obligatorio.'
  } else if (remito.length > LARGO_MAXIMO_REMITO) {
    errores.numeroRemito = `El número de remito no puede tener más de ${LARGO_MAXIMO_REMITO} caracteres.`
  }

  if (!datos.fecha) {
    errores.fecha = 'La fecha de recepción es obligatoria.'
  } else if (!fechaEnPalabras(datos.fecha)) {
    errores.fecha = 'Ingresá una fecha válida.'
  } else if (esFechaFutura(datos.fecha, ahora)) {
    errores.fecha = 'La fecha de recepción no puede ser futura.'
  }

  if (datos.observaciones.trim().length > LARGO_MAXIMO_OBSERVACIONES) {
    errores.observaciones = `Las observaciones no pueden tener más de ${LARGO_MAXIMO_OBSERVACIONES} caracteres.`
  }

  return errores
}

export function hayErrores(errores: ErroresRecepcion): boolean {
  return Object.keys(errores).length > 0
}
