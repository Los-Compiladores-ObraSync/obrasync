import { describe, expect, it } from 'vitest'
import { textoOrdenesBloqueadas, textoPendientes } from './textos'

describe('textos de notificaciones', () => {
  it('formatea textoPendientes en singular y plural', () => {
    expect(textoPendientes(1)).toBe('Tenés 1 requisición para aprobar')
    expect(textoPendientes(2)).toBe('Tenés 2 requisiciones para aprobar')
    expect(textoPendientes(5)).toBe('Tenés 5 requisiciones para aprobar')
  })

  it('formatea textoOrdenesBloqueadas en singular y plural', () => {
    expect(textoOrdenesBloqueadas(1)).toBe('1 orden de compra bloqueada por presupuesto')
    expect(textoOrdenesBloqueadas(2)).toBe('2 órdenes de compra bloqueadas por presupuesto')
    expect(textoOrdenesBloqueadas(5)).toBe('5 órdenes de compra bloqueadas por presupuesto')
  })
})
