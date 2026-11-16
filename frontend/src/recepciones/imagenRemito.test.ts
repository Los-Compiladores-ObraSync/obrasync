import { describe, expect, it } from 'vitest'
import { LADO_MAXIMO_PX, reducirImagen, tamanoReducido } from './imagenRemito'

describe('tamanoReducido', () => {
  it('achica una foto grande manteniendo la proporción', () => {
    expect(tamanoReducido(4000, 3000)).toEqual({ ancho: LADO_MAXIMO_PX, alto: 1200 })
    expect(tamanoReducido(3000, 4000)).toEqual({ ancho: 1200, alto: LADO_MAXIMO_PX })
  })

  it('no agranda una imagen que ya es chica', () => {
    expect(tamanoReducido(800, 600)).toEqual({ ancho: 800, alto: 600 })
    expect(tamanoReducido(1600, 1600)).toEqual({ ancho: 1600, alto: 1600 })
  })

  it('nunca devuelve un lado de 0 píxeles', () => {
    expect(tamanoReducido(100000, 10).alto).toBeGreaterThanOrEqual(1)
  })
})

describe('reducirImagen', () => {
  it('rechaza un archivo que no es una imagen, con un texto para la persona', async () => {
    const pdf = new File(['%PDF-1.7'], 'remito.pdf', { type: 'application/pdf' })

    await expect(reducirImagen(pdf)).rejects.toThrow(/no es una imagen/)
  })
})
