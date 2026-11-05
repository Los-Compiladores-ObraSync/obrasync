import { validarEmail } from '../auth/validacionEmail'

// Mínimo de la regla actual de Identity (igual que el registro y restablecer contraseña).
export const LARGO_MINIMO_PASSWORD = 6

export interface ErroresCapataz {
  nombreCompleto?: string
  email?: string
  password?: string
}

export function validarCapataz(datos: {
  nombreCompleto: string
  email: string
  password: string
}): ErroresCapataz {
  const errores: ErroresCapataz = {}
  if (!datos.nombreCompleto.trim()) errores.nombreCompleto = 'El nombre completo es obligatorio.'
  const errorEmail = validarEmail(datos.email)
  if (errorEmail) errores.email = errorEmail
  if (!datos.password) {
    errores.password = 'La contraseña inicial es obligatoria.'
  } else if (datos.password.length < LARGO_MINIMO_PASSWORD) {
    errores.password = `La contraseña debe tener al menos ${LARGO_MINIMO_PASSWORD} caracteres.`
  }
  return errores
}

export function hayErroresCapataz(errores: ErroresCapataz): boolean {
  return Object.keys(errores).length > 0
}
