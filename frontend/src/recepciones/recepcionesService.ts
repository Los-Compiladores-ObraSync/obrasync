import { apiFetch, apiFetchBlob, apiFetchPagina, TIEMPO_LIMITE_ASISTENTE_MS } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import type {
  CrearRecepcionRequest,
  EntregaConProblemaRequest,
  LecturaRemito,
  OrdenPendiente,
  RecepcionesDeOrden,
  RecepcionHistorial,
  RecepcionRegistrada,
} from './recepcionesTipos'

/**
 * Órdenes de compra que todavía no se recibieron por completo.
 * Fuente: GET /api/recepciones/ordenes-pendientes (Capataz, Comprador y Admin).
 */
export function obtenerOrdenesPendientes(): Promise<OrdenPendiente[]> {
  // Trae todas las páginas (#172): el Capataz elige la orden entre todas las pendientes.
  return traerTodas((pagina) =>
    apiFetchPagina<OrdenPendiente>(conPaginacion('/api/recepciones/ordenes-pendientes', pagina)),
  )
}

/**
 * Registra una recepción total o parcial contra una orden de compra, con su remito.
 * Fuente: POST /api/recepciones (solo Capataz y Admin).
 *
 * Errores: 400 si los datos no sirven o se excede el saldo;
 * 409 si la orden ya se recibió por completo o el número de remito ya se usó en esa orden.
 */
export function registrarRecepcion(datos: CrearRecepcionRequest): Promise<RecepcionRegistrada> {
  return apiFetch<RecepcionRegistrada>('/api/recepciones', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}

/** Historial de una orden o de todas las obras accesibles para el usuario. */
export function obtenerHistorialRecepciones(ordenCompraId?: string): Promise<RecepcionHistorial[]> {
  const ruta = ordenCompraId
    ? `/api/recepciones?ordenCompraId=${encodeURIComponent(ordenCompraId)}`
    : '/api/recepciones'
  return traerTodas((pagina) =>
    apiFetchPagina<RecepcionHistorial>(conPaginacion(ruta, pagina)),
  )
}

/**
 * Lee el número y la fecha de la foto de un remito (#209).
 * Fuente: POST /api/asistente/remitos/lecturas (multipart, campo "imagen"; Capataz y Admin).
 * Devuelve una SUGERENCIA: no guarda nada, y cada campo es null si no se pudo leer.
 *
 * Errores: 400 si la imagen no sirve; 429 si hay demasiadas fotos o el asistente está ocupado;
 * 503 si el asistente no está disponible (se carga a mano).
 */
export function leerRemitoDeFoto(imagen: Blob): Promise<LecturaRemito> {
  const formulario = new FormData()
  formulario.append('imagen', imagen, 'remito.jpg')
  return apiFetch<LecturaRemito>('/api/asistente/remitos/lecturas', {
    method: 'POST',
    body: formulario,
    // El modelo tarda más que un pedido normal (el servidor le da hasta 30 s).
    tiempoLimiteMs: TIEMPO_LIMITE_ASISTENTE_MS,
  })
}

/** Subir hasta 3 fotos con mala señal en la obra tarda más que un pedido normal (15 s): se le da hasta 1 minuto. */
export const TIEMPO_LIMITE_SUBIDA_FOTOS_MS = 60_000

/**
 * Registra una entrega con problema (#217): "no llegó nada", "llegó incompleta", "llegó dañada" o "no es lo
 * pedido", con hasta 3 fotos de evidencia. No cambia lo recibido ni el saldo: la orden sigue esperando la mercadería.
 * Fuente: POST /api/recepciones/problemas (multipart; solo Capataz y Admin).
 *
 * Las fotos tienen que venir ya achicadas (ver imagenEvidencia.ts: JPEG de hasta 2 MB cada una).
 * Errores: 400 si el motivo, el detalle o las fotos no sirven; 403 si la obra no es del Capataz;
 * 409 si la orden está bloqueada, todavía no se despachó o ya se recibió por completo.
 */
export function registrarEntregaConProblema(datos: EntregaConProblemaRequest, fotos: Blob[]): Promise<RecepcionRegistrada> {
  const formulario = new FormData()
  formulario.append('obraId', datos.obraId)
  formulario.append('ordenCompraId', datos.ordenCompraId)
  formulario.append('motivo', datos.motivo)
  const detalle = datos.detalle?.trim()
  if (detalle) formulario.append('detalle', detalle)
  const numeroRemito = datos.numeroRemito?.trim()
  if (numeroRemito) formulario.append('numeroRemito', numeroRemito)
  // El campo se llama "fotos" y se repite una vez por foto (así lo define el contrato).
  fotos.forEach((foto, indice) => formulario.append('fotos', foto, `foto-${indice + 1}.jpg`))

  return apiFetch<RecepcionRegistrada>('/api/recepciones/problemas', {
    method: 'POST',
    body: formulario,
    tiempoLimiteMs: TIEMPO_LIMITE_SUBIDA_FOTOS_MS,
  })
}

/**
 * La orden con su despacho (fecha estimada y observación), lo que falta recibir y el historial completo de
 * recepciones y problemas, con los datos de sus fotos.
 * Fuente: GET /api/recepciones/ordenes/{ordenCompraId} (Capataz, Comprador, Admin y Proveedor dueño de la orden).
 * Errores: 403 si la orden no es de la persona, 404 si no existe.
 */
export function obtenerRecepcionesDeOrden(ordenCompraId: string): Promise<RecepcionesDeOrden> {
  return apiFetch<RecepcionesDeOrden>(`/api/recepciones/ordenes/${encodeURIComponent(ordenCompraId)}`)
}

/**
 * Baja una foto de evidencia. La ruta exige sesión, así que no se puede usar directo en un <img>: se baja
 * con el token y se muestra con URL.createObjectURL (y URL.revokeObjectURL al terminar).
 * Fuente: GET /api/recepciones/{recepcionId}/fotos/{fotoId}.
 */
export function descargarFotoEvidencia(recepcionId: string, fotoId: string): Promise<Blob> {
  return apiFetchBlob(`/api/recepciones/${encodeURIComponent(recepcionId)}/fotos/${encodeURIComponent(fotoId)}`, {
    tiempoLimiteMs: TIEMPO_LIMITE_SUBIDA_FOTOS_MS,
  })
}
