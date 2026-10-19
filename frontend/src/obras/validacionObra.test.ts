import { describe, expect, it } from 'vitest'
import { fechaAIso, hayErrores, validarObra, type DatosObraForm } from './validacionObra'

const valido: DatosObraForm = {
  nombre: 'Torre Norte',
  ubicacion: 'Av. Siempre Viva 742',
  fechaInicio: '2026-11-02',
  capatazId: 'a1b2c3d4-0000-0000-0000-000000000001',
  etapas: ['Cimientos', 'Estructura'],
}

describe('validarObra', () => {
  it('no marca errores con datos completos', () => {
    expect(hayErrores(validarObra(valido))).toBe(false)
  })

  it('pide los campos de texto obligatorios', () => {
    const e = validarObra({ ...valido, nombre: '  ', ubicacion: '' })
    expect(e.nombre).toBeDefined()
    expect(e.ubicacion).toBeDefined()
  })

  it('pide la fecha de inicio', () => {
    expect(validarObra({ ...valido, fechaInicio: '' }).fechaInicio).toBeDefined()
  })

  it('rechaza una fecha con formato inválido', () => {
    expect(validarObra({ ...valido, fechaInicio: '2026-13-45' }).fechaInicio).toBeDefined()
  })

  it('pide un capataz con formato de GUID', () => {
    expect(validarObra({ ...valido, capatazId: '' }).capatazId).toBeDefined()
    expect(validarObra({ ...valido, capatazId: 'juan' }).capatazId).toBeDefined()
  })

  it('exige al menos una etapa', () => {
    expect(validarObra({ ...valido, etapas: [] }).etapas).toBeDefined()
  })

  it('marca la etapa vacía por posición', () => {
    const e = validarObra({ ...valido, etapas: ['Cimientos', ' '] })
    expect(e.etapasItems).toEqual({ 1: expect.any(String) })
  })

  it('marca la etapa repetida sin distinguir mayúsculas', () => {
    const e = validarObra({ ...valido, etapas: ['Cimientos', 'cimientos'] })
    expect(e.etapasItems?.[1]).toBeDefined()
    expect(e.etapasItems?.[0]).toBeUndefined()
  })
})

describe('fechaAIso', () => {
  it('arma un date-time en UTC', () => {
    expect(fechaAIso('2026-11-02')).toBe('2026-11-02T00:00:00.000Z')
  })
})
