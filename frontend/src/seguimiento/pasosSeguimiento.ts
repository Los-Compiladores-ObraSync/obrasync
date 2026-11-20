import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import type { SeguimientoRequisicion } from './seguimientoService'

export type EstadoPaso = 'hecho' | 'actual' | 'pendiente' | 'problema'

export interface Paso {
  clave: string
  titulo: string
  detalle?: string
  estado: EstadoPaso
}

/** Una recepción ya registrada (solo lo que hace falta para el seguimiento). */
export interface RecepcionResumen {
  estado?: number
}

// Estados de una recepción en el backend: 1 Pendiente, 2 Parcial, 3 Completa.
const RECEPCION_PARCIAL = 2
const RECEPCION_COMPLETA = 3

function fecha(iso?: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString('es-AR', { day: 'numeric', month: 'long' })
}

/**
 * Convierte lo que sabe cada servicio en una lista de pasos que se lee de arriba hacia abajo.
 * Es una función pura (sin pantalla ni red) para poder probar cada caso sin armar la página.
 */
export function armarPasos(
  requisicion: RequisicionResponseDto,
  seguimiento: SeguimientoRequisicion | null,
  recepciones: RecepcionResumen[],
): Paso[] {
  const pasos: Paso[] = [
    {
      clave: 'enviado',
      titulo: 'Pedido enviado',
      detalle: fecha(requisicion.fechaCreacion) ? `El ${fecha(requisicion.fechaCreacion)}` : undefined,
      estado: 'hecho',
    },
  ]

  if (requisicion.estado === 'Pendiente') {
    pasos.push({ clave: 'compras', titulo: 'Esperando que Compras lo apruebe', estado: 'actual' })
    return pasos
  }
  if (requisicion.estado === 'Rechazada') {
    pasos.push({
      clave: 'compras',
      titulo: 'Compras lo rechazó',
      detalle: requisicion.motivoRechazo ?? 'Sin motivo informado',
      estado: 'problema',
    })
    return pasos
  }
  if (requisicion.estado === 'Cancelada') {
    pasos.push({ clave: 'compras', titulo: 'Cancelaste el pedido', estado: 'problema' })
    return pasos
  }

  pasos.push({
    clave: 'compras',
    titulo: 'Compras lo aprobó',
    detalle: fecha(requisicion.fechaAprobacion) ? `El ${fecha(requisicion.fechaAprobacion)}` : undefined,
    estado: 'hecho',
  })

  const ordenes = seguimiento?.ordenesCompra ?? []
  const ofertas = seguimiento?.cantidadCotizaciones ?? 0
  const textoOfertas = ofertas === 1 ? '1 oferta recibida' : `${ofertas} ofertas recibidas`

  // 3. Licitación
  if (!seguimiento?.licitacionId) {
    pasos.push({ clave: 'licitacion', titulo: 'Compras todavía no pidió cotizaciones', estado: 'actual' })
  } else if (seguimiento.estadoLicitacion === 'Abierta') {
    const cierre = fecha(seguimiento.fechaLimiteCotizacion)
    pasos.push({
      clave: 'licitacion',
      titulo: 'Los proveedores están cotizando',
      detalle: `${textoOfertas}${cierre ? ` · cierra el ${cierre}` : ''}`,
      estado: 'actual',
    })
  } else if (seguimiento.estadoLicitacion === 'Cerrada') {
    pasos.push({
      clave: 'licitacion',
      titulo: 'Cotizaciones cerradas: Compras está eligiendo',
      detalle: textoOfertas,
      estado: 'actual',
    })
  } else {
    pasos.push({ clave: 'licitacion', titulo: 'Compras eligió la mejor oferta', detalle: textoOfertas, estado: 'hecho' })
  }

  // 4. Orden de compra
  if (ordenes.length === 0) {
    pasos.push({ clave: 'orden', titulo: 'Orden de compra', detalle: 'Todavía no se emitió', estado: 'pendiente' })
    return pasos
  }
  const proveedores = [...new Set(ordenes.map((o) => o.proveedorNombre).filter(Boolean))].join(', ')
  pasos.push({
    clave: 'orden',
    titulo: ordenes.length === 1 ? 'Orden de compra emitida' : `${ordenes.length} órdenes de compra emitidas`,
    detalle: proveedores || undefined,
    estado: 'hecho',
  })

  // 5. Despacho
  const despachadas = ordenes.filter((o) => o.estado === 'Despachada' || o.fechaDespacho)
  if (despachadas.length === ordenes.length) {
    const ultima = despachadas.map((o) => o.fechaDespacho).filter(Boolean).sort().at(-1)
    pasos.push({
      clave: 'despacho',
      titulo: 'El proveedor despachó la mercadería',
      detalle: fecha(ultima) ? `El ${fecha(ultima)}` : undefined,
      estado: 'hecho',
    })
  } else {
    pasos.push({
      clave: 'despacho',
      titulo: despachadas.length > 0 ? 'Despacho parcial' : 'Esperando que el proveedor despache',
      estado: 'actual',
    })
  }

  // 6. Recepción en obra
  if (recepciones.some((r) => r.estado === RECEPCION_COMPLETA)) {
    pasos.push({ clave: 'recepcion', titulo: 'Recibido en obra', detalle: 'La orden quedó completa', estado: 'hecho' })
  } else if (recepciones.some((r) => r.estado === RECEPCION_PARCIAL)) {
    pasos.push({ clave: 'recepcion', titulo: 'Recibiste una parte', detalle: 'Falta mercadería por llegar', estado: 'actual' })
  } else {
    pasos.push({
      clave: 'recepcion',
      titulo: 'Recepción en obra',
      detalle: despachadas.length > 0 ? 'Esperando que llegue' : 'Todavía no hay entregas',
      estado: despachadas.length > 0 ? 'actual' : 'pendiente',
    })
  }

  return pasos
}
