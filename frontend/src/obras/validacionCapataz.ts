import { validarEmail } from '../auth/validacionEmail'
import { errorDePasswordNueva } from '../auth/validacionPassword'

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
  } else {
    const errorPassword = errorDePasswordNueva(datos.password)
    if (errorPassword) errores.password = errorPassword
  }
  return errores
}

export function hayErroresCapataz(errores: ErroresCapataz): boolean {
  return Object.keys(errores).length > 0
}
