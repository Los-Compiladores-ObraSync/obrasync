import { describe, expect, it } from 'vitest'
import { MAX_FOTOS_EVIDENCIA } from './imagenEvidencia'
import { MOTIVOS_PROBLEMA } from './recepcionesTipos'
import {
  esMotivoProblema,
  hayErroresProblema,
  LARGO_MAXIMO_DETALLE_PROBLEMA,
  validarEntregaConProblema,
} from './validacionEntregaConProblema'

describe('validarEntregaConProblema', () => {
  it('acepta un motivo con detalle y fotos dentro de los límites', () => {
    const errores = validarEntregaConProblema({ motivo: 'Llegó dañada', detalle: '3 bolsas rotas', cantidadFotos: 2 })

    expect(errores).toEqual({})
    expect(hayErroresProblema(errores)).toBe(false)
  })

  it('el detalle y las fotos son opcionales', () => {
    expect(validarEntregaConProblema({ motivo: 'No llegó nada', detalle: '', cantidadFotos: 0 })).toEqual({})
  })

  it('pide elegir un motivo', () => {
    const errores = validarEntregaConProblema({ motivo: '', detalle: '', cantidadFotos: 0 })

    expect(errores.motivo).toMatch(/Elegí/)
    expect(hayErroresProblema(errores)).toBe(true)
  })

  it('el detalle puede tener 500 caracteres pero no 501', () => {
    const justo = 'a'.repeat(LARGO_MAXIMO_DETALLE_PROBLEMA)

    expect(validarEntregaConProblema({ motivo: 'Llegó incompleta', detalle: justo, cantidadFotos: 0 }).detalle).toBeUndefined()
    expect(validarEntregaConProblema({ motivo: 'Llegó incompleta', detalle: `${justo}a`, cantidadFotos: 0 }).detalle).toMatch(/500/)
  })

  it('no cuenta los espacios de los costados del detalle', () => {
    const detalle = `  ${'a'.repeat(LARGO_MAXIMO_DETALLE_PROBLEMA)}  `

    expect(validarEntregaConProblema({ motivo: 'Llegó incompleta', detalle, cantidadFotos: 0 }).detalle).toBeUndefined()
  })

  it('acepta hasta 3 fotos y rechaza la cuarta', () => {
    expect(validarEntregaConProblema({ motivo: 'Llegó dañada', detalle: '', cantidadFotos: MAX_FOTOS_EVIDENCIA }).fotos).toBeUndefined()
    expect(validarEntregaConProblema({ motivo: 'Llegó dañada', detalle: '', cantidadFotos: MAX_FOTOS_EVIDENCIA + 1 }).fotos).toMatch(/3 fotos/)
  })
})

describe('esMotivoProblema', () => {
  it('reconoce los cuatro motivos del contrato, con su texto exacto', () => {
    expect(MOTIVOS_PROBLEMA).toEqual(['No llegó nada', 'Llegó incompleta', 'Llegó dañada', 'No es lo pedido'])
    for (const motivo of MOTIVOS_PROBLEMA) expect(esMotivoProblema(motivo)).toBe(true)
  })

  it('rechaza cualquier otro texto', () => {
    expect(esMotivoProblema('Otro motivo')).toBe(false)
    expect(esMotivoProblema('')).toBe(false)
    expect(esMotivoProblema('llegó dañada')).toBe(false)
  })
})
