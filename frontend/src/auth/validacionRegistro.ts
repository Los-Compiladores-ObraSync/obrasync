import { validarEmail } from './validacionEmail'
import { errorDePasswordNueva } from './validacionPassword'

export interface ErroresRegistro {
  nombreCompleto?: string
  email?: string
  password?: string
}

export function validarRegistro(nombreCompleto: string, email: string, password: string): ErroresRegistro {
  const errores: ErroresRegistro = {}
  if (!nombreCompleto.trim()) errores.nombreCompleto = 'El nombre completo es obligatorio.'
  const errorEmail = validarEmail(email)
  if (errorEmail) errores.email = errorEmail
  if (!password) errores.password = 'La contraseña es obligatoria.'
  else {
    const errorPassword = errorDePasswordNueva(password)
    if (errorPassword) errores.password = errorPassword
  }
  return errores
}
