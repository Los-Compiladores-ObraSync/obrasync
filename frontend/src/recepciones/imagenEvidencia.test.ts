import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  CALIDADES_FOTO_EVIDENCIA,
  MAX_BYTES_FOTO_EVIDENCIA,
  MAX_FOTOS_EVIDENCIA,
  reducirFotoEvidencia,
} from './imagenEvidencia'
import { ErrorDeImagen, LADO_MAXIMO_PX } from './imagenRemito'

const MB = 1024 * 1024

/**
 * jsdom no dibuja en un canvas: se reemplaza por uno falso que "codifica" la imagen con el peso que se le indique
 * en cada intento. Así se prueba la lógica (tamaño, calidades, límite de 2 MB) sin un navegador real.
 */
function prepararNavegadorFalso(pesosPorIntento: number[], ladoOriginal = { ancho: 4000, alto: 3000 }) {
  const calidadesPedidas: (number | undefined)[] = []
  const pesos = [...pesosPorIntento]
  const lienzo = {
    width: 0,
    height: 0,
    getContext: () => ({ drawImage: vi.fn() }),
    toBlob: (devolver: BlobCallback, _tipo?: string, calidad?: number) => {
      calidadesPedidas.push(calidad)
      const peso = pesos.shift() ?? 0
      devolver(peso > 0 ? new Blob([new Uint8Array(peso)], { type: 'image/jpeg' }) : null)
    },
  }
  const bitmap = { width: ladoOriginal.ancho, height: ladoOriginal.alto, close: vi.fn() }
  vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue(bitmap))
  const crearElemento = document.createElement.bind(document)
  vi.spyOn(document, 'createElement').mockImplementation((nombre: string, opciones?: ElementCreationOptions) =>
    nombre === 'canvas' ? (lienzo as unknown as HTMLCanvasElement) : crearElemento(nombre, opciones),
  )
  return { lienzo, bitmap, calidadesPedidas }
}

const foto = () => new File(['x'], 'foto.jpg', { type: 'image/jpeg' })

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('límites de las fotos de evidencia', () => {
  it('coinciden con el contrato de Recepción: 3 fotos de hasta 2 MB', () => {
    expect(MAX_FOTOS_EVIDENCIA).toBe(3)
    expect(MAX_BYTES_FOTO_EVIDENCIA).toBe(2 * MB)
  })
})

describe('reducirFotoEvidencia', () => {
  it('rechaza un archivo que no es una imagen, con un texto para la persona', async () => {
    const pdf = new File(['%PDF-1.7'], 'remito.pdf', { type: 'application/pdf' })

    await expect(reducirFotoEvidencia(pdf)).rejects.toThrow(/no es una foto/)
    await expect(reducirFotoEvidencia(pdf)).rejects.toBeInstanceOf(ErrorDeImagen)
  })

  it('achica la foto a 1600 px de lado mayor y la devuelve en JPEG', async () => {
    const { lienzo, bitmap } = prepararNavegadorFalso([500 * 1024])

    const resultado = await reducirFotoEvidencia(foto())

    expect(lienzo.width).toBe(LADO_MAXIMO_PX)
    expect(lienzo.height).toBe(1200)
    expect(resultado.type).toBe('image/jpeg')
    expect(resultado.size).toBe(500 * 1024)
    expect(bitmap.close).toHaveBeenCalled()
  })

  it('usa la mejor calidad cuando la foto ya entra en 2 MB', async () => {
    const { calidadesPedidas } = prepararNavegadorFalso([1 * MB])

    await reducirFotoEvidencia(foto())

    expect(calidadesPedidas).toEqual([CALIDADES_FOTO_EVIDENCIA[0]])
  })

  it('baja la calidad si la primera pasa de 2 MB', async () => {
    const { calidadesPedidas } = prepararNavegadorFalso([3 * MB, 1 * MB])

    const resultado = await reducirFotoEvidencia(foto())

    expect(calidadesPedidas).toEqual([CALIDADES_FOTO_EVIDENCIA[0], CALIDADES_FOTO_EVIDENCIA[1]])
    expect(resultado.size).toBe(1 * MB)
  })

  it('acepta una foto de exactamente 2 MB', async () => {
    prepararNavegadorFalso([MAX_BYTES_FOTO_EVIDENCIA])

    const resultado = await reducirFotoEvidencia(foto())

    expect(resultado.size).toBe(MAX_BYTES_FOTO_EVIDENCIA)
  })

  it('se rinde si no entra en 2 MB ni con la peor calidad', async () => {
    const { calidadesPedidas, bitmap } = prepararNavegadorFalso([3 * MB, 3 * MB, 3 * MB])

    await expect(reducirFotoEvidencia(foto())).rejects.toThrow(/demasiado pesada/)

    expect(calidadesPedidas).toEqual([...CALIDADES_FOTO_EVIDENCIA])
    expect(bitmap.close).toHaveBeenCalled()
  })

  it('avisa si el navegador no puede abrir la foto', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn().mockRejectedValue(new Error('formato raro')))

    await expect(reducirFotoEvidencia(foto())).rejects.toThrow(/No pude abrir esa foto/)
  })

  it('avisa si no se pudo generar la imagen', async () => {
    const { bitmap } = prepararNavegadorFalso([0])

    await expect(reducirFotoEvidencia(foto())).rejects.toThrow(/No pude preparar la foto/)
    expect(bitmap.close).toHaveBeenCalled()
  })

  it('no agranda una foto chica', async () => {
    const { lienzo } = prepararNavegadorFalso([100 * 1024], { ancho: 800, alto: 600 })

    await reducirFotoEvidencia(foto())

    expect(lienzo.width).toBe(800)
    expect(lienzo.height).toBe(600)
  })
})
