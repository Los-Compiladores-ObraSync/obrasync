// Política de contraseñas del servicio de Usuarios (tarjeta #168): mínimo 8 caracteres y al menos un número.
// Es la única definición en el frontend: registro, cambiar, restablecer y crear capataz la usan, así el
// formulario avisa lo mismo que después exigiría el servidor.
export const LARGO_MINIMO_PASSWORD = 8

/** Devuelve el mensaje de error si la contraseña no cumple la política, o undefined si está bien. */
export function errorDePasswordNueva(password: string): string | undefined {
  if (password.length < LARGO_MINIMO_PASSWORD) {
    return `La contraseña debe tener al menos ${LARGO_MINIMO_PASSWORD} caracteres.`
  }
  if (!/\d/.test(password)) return 'La contraseña debe incluir al menos un número.'
  return undefined
}
