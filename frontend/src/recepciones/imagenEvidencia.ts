// Fotos de evidencia de una entrega con problema (#224). Reutiliza el achicado de las fotos de remito
// (imagenRemito.ts): lado mayor de 1600 px y JPEG nuevo, que además descarta la ubicación de la foto original.
// La diferencia: el servicio de Recepción acepta hasta 2 MB por foto y hasta 3 fotos por entrega con problema
// (contrato: POST /api/recepciones/problemas), así que se prueban calidades más bajas si la primera no entra.

import { reducirAJpeg, type TextosErrorImagen } from './imagenRemito'

/** Lo que acepta el servicio por foto (2 MB). */
export const MAX_BYTES_FOTO_EVIDENCIA = 2 * 1024 * 1024
/** Cuántas fotos acepta el servicio por entrega con problema. */
export const MAX_FOTOS_EVIDENCIA = 3
/** De la mejor a la peor: se usa la primera que entra en 2 MB. A 1600 px casi siempre alcanza con la primera. */
export const CALIDADES_FOTO_EVIDENCIA = [0.82, 0.7, 0.55] as const

const TEXTOS_EVIDENCIA: TextosErrorImagen = {
  noEsImagen: 'Ese archivo no es una foto. Elegí una imagen.',
  noSePudoAbrir: 'No pude abrir esa foto. Probá con otra.',
  noSePudoPreparar: 'No pude preparar la foto. Probá con otra.',
  demasiadoPesada: 'La foto es demasiado pesada. Probá con otra.',
}

/** Prepara una foto de evidencia para subirla. Lanza ErrorDeImagen con un texto listo para mostrar. */
export function reducirFotoEvidencia(archivo: File): Promise<Blob> {
  return reducirAJpeg(archivo, {
    maxBytes: MAX_BYTES_FOTO_EVIDENCIA,
    calidades: CALIDADES_FOTO_EVIDENCIA,
    textos: TEXTOS_EVIDENCIA,
  })
}
