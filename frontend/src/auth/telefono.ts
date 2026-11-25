// Teléfono opcional del proveedor: sirve para abrirle WhatsApp con el enlace de cotización ya escrito.
// Mismas reglas que el backend (RegisterRequestDto.Telefono): números, espacios, guiones, paréntesis y un + inicial.

const FORMATO_TELEFONO = /^\+?[0-9 ()-]{8,20}$/

export const AYUDA_TELEFONO = 'Con código de país, por ejemplo +54 9 11 5555 1234.'

/** El teléfono es opcional: vacío es válido. */
export function validarTelefono(telefono: string): string | undefined {
  const limpio = telefono.trim()
  if (!limpio) return undefined
  if (!FORMATO_TELEFONO.test(limpio)) {
    return 'Escribí solo números, con el código de país al inicio (por ejemplo +54 9 11 5555 1234).'
  }
  return undefined
}

/**
 * Número en el formato que pide wa.me (solo dígitos, con código de país) o null si no se puede asegurar:
 * sin "+" (o "00") no sabemos el país, así que no inventamos uno.
 */
export function digitosParaWhatsApp(telefono: string | null | undefined): string | null {
  const limpio = (telefono ?? '').trim()
  if (!limpio || validarTelefono(limpio)) return null
  let internacional: string | null = null
  if (limpio.startsWith('+')) internacional = limpio.slice(1)
  else if (limpio.startsWith('00')) internacional = limpio.slice(2)
  if (internacional === null) return null
  const digitos = internacional.replace(/\D/g, '')
  return digitos.length >= 8 ? digitos : null
}

/** Enlace que abre WhatsApp con el mensaje escrito: directo al chat si hay número, o para elegir contacto si no. */
export function urlWhatsApp(telefono: string | null | undefined, mensaje: string): string {
  const digitos = digitosParaWhatsApp(telefono)
  return `https://wa.me/${digitos ?? ''}?text=${encodeURIComponent(mensaje)}`
}
