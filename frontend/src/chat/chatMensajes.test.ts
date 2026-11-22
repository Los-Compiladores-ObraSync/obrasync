import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/ApiError'
import { mensajeDeErrorChat, puedeResolverProblema, unirMensajes, validarMensaje } from './chatMensajes'
import type { MensajeChatOrdenDto } from './chatTipos'

function mensaje(id: string, fechaCreacion: string, texto = id): MensajeChatOrdenDto {
  return { id, ordenCompraId: 'o1', autorId: 'u1', autorNombre: 'Ana', autorRol: 'Comprador', esSistema: false, texto, fechaCreacion }
}

describe('unirMensajes', () => {
  it('no repite un mensaje que llega por dos caminos y lo deja ordenado', () => {
    const a = mensaje('a', '2026-11-22T10:00:00Z')
    const b = mensaje('b', '2026-11-22T10:05:00Z')
    const c = mensaje('c', '2026-11-22T10:02:00Z')
    expect(unirMensajes([a, b], [c, b]).map((m) => m.id)).toEqual(['a', 'c', 'b'])
  })
})

describe('validarMensaje', () => {
  it('rechaza el texto vacío o solo con espacios', () => {
    expect(validarMensaje('')).not.toBeNull()
    expect(validarMensaje('   ')).not.toBeNull()
  })
  it('acepta hasta 1000 caracteres y rechaza más', () => {
    expect(validarMensaje('a'.repeat(1000))).toBeNull()
    expect(validarMensaje('a'.repeat(1001))).toContain('1000')
  })
})

describe('puedeResolverProblema', () => {
  it('solo Comprador y Admin', () => {
    expect(puedeResolverProblema('Comprador')).toBe(true)
    expect(puedeResolverProblema('Admin')).toBe(true)
    expect(puedeResolverProblema('Proveedor')).toBe(false)
    expect(puedeResolverProblema('Capataz')).toBe(false)
    expect(puedeResolverProblema(undefined)).toBe(false)
  })
})

describe('mensajeDeErrorChat', () => {
  it('traduce 403 y los errores de red', () => {
    expect(mensajeDeErrorChat(new ApiError(403, { title: 'Acceso denegado' }))).toContain('No tenés acceso')
    expect(mensajeDeErrorChat(new Error('red'))).toContain('No se pudo conectar')
  })
})
