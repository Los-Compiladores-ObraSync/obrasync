import { describe, expect, it } from 'vitest'
import { cantidadConUnidad, etiquetaUnidad } from './unidades'

describe('etiquetaUnidad', () => {
  it('muestra el nombre completo de las unidades del catálogo', () => {
    expect(etiquetaUnidad('BOLSA')).toBe('Bolsas')
    expect(etiquetaUnidad('KG')).toBe('Kilos')
    expect(etiquetaUnidad('M3')).toBe('Metros cúbicos (m³)')
    expect(etiquetaUnidad('UNIDAD')).toBe('Unidades')
  })

  it('si el código no se conoce, lo muestra tal cual', () => {
    expect(etiquetaUnidad('TN')).toBe('TN')
  })
})

describe('cantidadConUnidad', () => {
  it('usa singular con 1 y plural en los demás casos', () => {
    expect(cantidadConUnidad(1, 'BOLSA')).toBe('1 bolsa')
    expect(cantidadConUnidad(80, 'BOLSA')).toBe('80 bolsas')
    expect(cantidadConUnidad(2.5, 'M3')).toBe('2.5 metros cúbicos (m³)')
  })

  it('con un código desconocido deja el código', () => {
    expect(cantidadConUnidad(3, 'TN')).toBe('3 TN')
  })
})
