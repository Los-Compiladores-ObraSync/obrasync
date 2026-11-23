import { validarEmail } from './validacionEmail'

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
  else if (password.length < 8) errores.password = 'La contraseña debe tener al menos 8 caracteres.'
  else if (!/\d/.test(password)) errores.password = 'La contraseña debe incluir al menos un número.'
  return errores
}
