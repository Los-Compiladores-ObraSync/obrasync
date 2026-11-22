// Prepara la foto de un remito para mandarla a leer (#209). Los celulares sacan fotos de 3 a 10 MB: se achican en el
// propio dispositivo (mejor con mala señal en la obra, y la IA gasta menos tokens con una imagen más chica).
// El número de un remito se lee bien con ~1600 px de lado mayor.

/** Lado mayor de la imagen que se manda. */
export const LADO_MAXIMO_PX = 1600
/** Calidad JPEG (0 a 1): suficiente para leer texto impreso sin que pese de más. */
export const CALIDAD_JPEG = 0.82
/** Lo máximo que acepta el servidor. */
export const MAX_BYTES_SUBIDA = 5 * 1024 * 1024

/** Mide la imagen achicada manteniendo la proporción. Nunca agranda una imagen que ya es chica. */
export function tamanoReducido(ancho: number, alto: number, ladoMaximo: number = LADO_MAXIMO_PX) {
  const mayor = Math.max(ancho, alto)
  if (mayor <= ladoMaximo) return { ancho, alto }
  const escala = ladoMaximo / mayor
  return { ancho: Math.max(1, Math.round(ancho * escala)), alto: Math.max(1, Math.round(alto * escala)) }
}

/** Error con un texto listo para mostrarle a la persona. */
export class ErrorDeImagen extends Error {}

/** Los textos de error de cada uso (remito, evidencia…): se muestran tal cual a la persona. */
export interface TextosErrorImagen {
  noEsImagen: string
  noSePudoAbrir: string
  noSePudoPreparar: string
  demasiadoPesada: string
}

export interface OpcionesReduccion {
  /** Lo máximo que puede pesar la imagen que sale. */
  maxBytes: number
  /** Calidades JPEG a probar, de la mejor a la peor: se usa la primera que entra en maxBytes. */
  calidades: readonly number[]
  textos: TextosErrorImagen
}

/**
 * Achica una foto a un JPEG de hasta LADO_MAXIMO_PX y la vuelve a codificar desde cero. Volver a codificar
 * también descarta los metadatos de la foto original (por ejemplo, la ubicación donde se sacó).
 * Si el navegador no puede leerla, o no entra en maxBytes ni con la peor calidad, lanza ErrorDeImagen.
 */
export async function reducirAJpeg(archivo: File, opciones: OpcionesReduccion): Promise<Blob> {
  const { maxBytes, calidades, textos } = opciones
  if (!archivo.type.startsWith('image/')) {
    throw new ErrorDeImagen(textos.noEsImagen)
  }

  let bitmap: ImageBitmap
  try {
    // createImageBitmap respeta la orientación de la foto (EXIF) y no necesita mostrar la imagen en pantalla.
    bitmap = await createImageBitmap(archivo, { imageOrientation: 'from-image' })
  } catch {
    throw new ErrorDeImagen(textos.noSePudoAbrir)
  }

  try {
    const { ancho, alto } = tamanoReducido(bitmap.width, bitmap.height)
    const lienzo = document.createElement('canvas')
    lienzo.width = ancho
    lienzo.height = alto
    const contexto = lienzo.getContext('2d')
    if (!contexto) throw new ErrorDeImagen(textos.noSePudoPreparar)
    contexto.drawImage(bitmap, 0, 0, ancho, alto)

    for (const calidad of calidades) {
      const blob = await new Promise<Blob | null>((resolver) => lienzo.toBlob(resolver, 'image/jpeg', calidad))
      if (!blob || blob.size === 0) throw new ErrorDeImagen(textos.noSePudoPreparar)
      if (blob.size <= maxBytes) return blob
    }
    throw new ErrorDeImagen(textos.demasiadoPesada)
  } finally {
    bitmap.close()
  }
}

const TEXTOS_REMITO: TextosErrorImagen = {
  noEsImagen: 'El archivo no es una imagen. Sacale una foto al remito.',
  noSePudoAbrir: 'No pude abrir esa foto. Probá con otra o cargá el remito a mano.',
  noSePudoPreparar: 'No pude preparar la foto. Cargá el remito a mano.',
  demasiadoPesada: 'La foto es demasiado pesada. Probá con otra.',
}

/**
 * Convierte la foto elegida en un JPEG chico. Si el navegador no puede leerla (por ejemplo un formato raro),
 * lanza ErrorDeImagen: en ese caso se carga el remito a mano.
 */
export function reducirImagen(archivo: File): Promise<Blob> {
  return reducirAJpeg(archivo, { maxBytes: MAX_BYTES_SUBIDA, calidades: [CALIDAD_JPEG], textos: TEXTOS_REMITO })
}
