// Validación del campo email, compartida por las pantallas de login y de recuperar contraseña.

const RE_EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Mensaje de error para un email (undefined si está bien). */
export function validarEmail(email: string): string | undefined {
  if (!email.trim()) return 'El email es obligatorio.'
  if (!RE_EMAIL.test(email.trim())) return 'Ingresá un email válido.'
  return undefined
}
