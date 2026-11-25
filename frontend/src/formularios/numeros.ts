// Reglas comunes de los campos numéricos de la app.
//  - NUNCA se escribe una coma: el separador decimal es el punto ("2.5"). Así "1,4" no se confunde con otro número
//    y lo que se ve en pantalla es lo mismo que viaja al servidor.
//  - Las BOLSAS (y solo las bolsas) se piden, ofrecen y reciben en cantidades ENTERAS: no existe "1.4 bolsas".

import type { KeyboardEvent } from 'react'

/** Códigos de unidad del catálogo que solo admiten cantidades enteras. */
export const UNIDADES_ENTERAS: readonly string[] = ['BOLSA']

export const MENSAJE_SIN_COMA = 'Usá punto en lugar de coma para los decimales.'
export const MENSAJE_SOLO_ENTEROS = 'Las bolsas se cuentan enteras: escribí un número sin decimales.'

/** ¿Esta unidad del catálogo (BOLSA, M3, KG…) se cuenta solo en enteros? */
export function esUnidadEntera(unidad: string | null | undefined): boolean {
  return !!unidad && UNIDADES_ENTERAS.includes(unidad.trim().toUpperCase())
}

/**
 * ¿La presentación que ofrece el proveedor se cuenta en enteros? Es texto libre ("bolsa de 25 kg"):
 * se considera entera si el material pedido va en bolsas o si la presentación empieza con "bolsa".
 */
export function esPresentacionEntera(presentacion: string, unidadPedida?: string | null): boolean {
  return esUnidadEntera(unidadPedida) || /^\s*bolsas?\b/i.test(presentacion)
}

/**
 * ¿Se puede aceptar lo que la persona tiene escrito en el campo? No acepta comas, ni puntos si el campo es entero.
 * Se usa en el onChange de los campos de texto: si devuelve false, no se actualiza y el carácter "no aparece".
 */
export function aceptaTextoNumerico(valor: string, entero = false): boolean {
  if (valor.includes(',')) return false
  if (entero && valor.includes('.')) return false
  return true
}

/** Para los <input type="number">: frena la coma (y el punto en los enteros) antes de que se escriban. */
export function bloquearTeclasDecimales(evento: KeyboardEvent<HTMLInputElement>, entero = false): void {
  if (evento.key === ',' || (entero && evento.key === '.')) evento.preventDefault()
}

/**
 * Error de un número ya escrito, o undefined si está bien. Es la red de seguridad por si algo se pega o entra por otro camino.
 * No valida rangos: eso lo hace cada formulario.
 */
export function errorDeFormatoNumerico(valor: string, entero = false): string | undefined {
  if (valor.includes(',')) return MENSAJE_SIN_COMA
  if (entero && valor.includes('.')) return MENSAJE_SOLO_ENTEROS
  return undefined
}
