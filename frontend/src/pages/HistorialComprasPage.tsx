import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ListaMovimientos } from '../historialCompras/ListaMovimientos'
import { cargarMovimientosCompras, type MovimientoCompra, type TipoMovimiento } from '../historialCompras/movimientos'
import { diaHistorial, esDiaReal } from '../historialCompras/fechas'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { ObraResumen } from '../requisiciones/requisicionesTipos'
import styles from './HistorialComprasPage.module.css'

const TAMANO_PAGINA = 20
const TIPOS: Array<{ valor: TipoMovimiento | ''; etiqueta: string }> = [
  { valor: '', etiqueta: 'Todos los tipos' },
  { valor: 'requisiciones', etiqueta: 'Requisiciones' },
  { valor: 'licitaciones', etiqueta: 'Licitaciones' },
  { valor: 'ordenes', etiqueta: 'Órdenes de compra' },
  { valor: 'recepciones', etiqueta: 'Recepciones y problemas' },
]

function fechaDeUrl(valor: string | null): string {
  return esDiaReal(valor) ? valor : ''
}

function paginaDeUrl(valor: string | null): number {
  const pagina = Number(valor)
  return Number.isSafeInteger(pagina) && pagina > 0 ? pagina : 1
}

export function HistorialComprasPage() {
  const [params, setParams] = useSearchParams()
  const tipoParam = params.get('tipo') ?? ''
  const tipo = TIPOS.some((opcion) => opcion.valor === tipoParam) ? tipoParam : ''
  const obraId = params.get('obraId') ?? ''
  const desde = fechaDeUrl(params.get('desde'))
  const hasta = fechaDeUrl(params.get('hasta'))
  const pagina = paginaDeUrl(params.get('pagina'))
  const [movimientos, setMovimientos] = useState<MovimientoCompra[]>([])
  const [obras, setObras] = useState<ObraResumen[]>([])
  const [errores, setErrores] = useState<string[]>([])
  const [adjudicacionesSinFecha, setAdjudicacionesSinFecha] = useState(0)
  const [cargando, setCargando] = useState(true)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false
    void cargarMovimientosCompras().then((resultado) => {
      if (cancelado) return
      setMovimientos(resultado.movimientos)
      setErrores(resultado.errores)
      setAdjudicacionesSinFecha(resultado.adjudicacionesSinFecha)
    }).catch(() => {
      if (!cancelado) setErrores(['historial'])
    }).finally(() => { if (!cancelado) setCargando(false) })
    void obtenerObras().then((lista) => { if (!cancelado) setObras(lista) }).catch(() => {})
    return () => { cancelado = true }
  }, [intento])

  const filtrados = useMemo(() => movimientos.filter((item) => {
    const dia = diaHistorial(item.fecha)
    return (!tipo || item.tipo === tipo)
      && (!obraId || item.obraId === obraId)
      && dia !== null
      && (!desde || dia >= desde)
      && (!hasta || dia <= hasta)
  }), [movimientos, tipo, obraId, desde, hasta])
  const paginas = Math.max(1, Math.ceil(filtrados.length / TAMANO_PAGINA))
  const paginaVisible = Math.min(pagina, paginas)

  useEffect(() => {
    const siguiente = new URLSearchParams(params)
    for (const nombre of ['desde', 'hasta']) {
      const valor = siguiente.get(nombre)
      if (valor !== null && !esDiaReal(valor)) siguiente.delete(nombre)
    }

    const paginaOriginal = siguiente.get('pagina')
    if (paginaOriginal !== null) {
      const paginaBienFormada = /^[1-9]\d*$/.test(paginaOriginal) && Number.isSafeInteger(Number(paginaOriginal))
      if (!paginaBienFormada) {
        siguiente.delete('pagina')
      } else if (!cargando) {
        const destino = Math.min(Number(paginaOriginal), paginas)
        if (destino !== Number(paginaOriginal)) siguiente.set('pagina', String(destino))
      }
    }

    if (siguiente.toString() !== params.toString()) setParams(siguiente, { replace: true })
  }, [params, setParams, cargando, paginas])

  const visibles = filtrados.slice((paginaVisible - 1) * TAMANO_PAGINA, paginaVisible * TAMANO_PAGINA)
  const queryHistorial = new URLSearchParams()
  for (const nombre of ['tipo', 'obraId', 'desde', 'hasta', 'pagina']) {
    const valor = params.get(nombre)
    if (valor) queryHistorial.set(nombre, valor)
  }
  const movimientosVisibles = visibles.map((item) => {
    const destino = new URL(item.href, 'http://obrasync.local')
    for (const [nombre, valor] of queryHistorial) destino.searchParams.set(nombre, valor)
    if (item.tipo === 'ordenes' || item.tipo === 'licitaciones') destino.searchParams.set('desdeHistorial', '1')
    return { ...item, href: `${destino.pathname}${destino.search}` }
  })
  const nombresObras = Object.fromEntries(obras.map((obra) => [obra.id, obra.nombre]))

  function actualizar(filtros: Record<string, string>) {
    const siguiente = new URLSearchParams(params)
    for (const [nombre, valor] of Object.entries(filtros)) {
      if (valor) siguiente.set(nombre, valor)
      else siguiente.delete(nombre)
    }
    siguiente.delete('pagina')
    setParams(siguiente, { replace: true })
  }

  function irAPagina(valor: number) {
    const siguiente = new URLSearchParams(params)
    if (valor > 1) siguiente.set('pagina', String(valor))
    else siguiente.delete('pagina')
    setParams(siguiente, { replace: true })
  }

  const hayFiltros = Boolean(tipo || obraId || desde || hasta)

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>← Volver a Compras</Link>
      <h1>Historial de Compras</h1>
      <p className="intro">Movimientos con fecha disponible en los listados y seguimientos actuales. El alcance puede ser incompleto: los servicios recuperan como máximo 20 páginas (hasta 1.000 registros) por listado y no informan a esta pantalla si se alcanzó ese tope ni qué fuente quedó limitada.</p>

      <div className={styles.filtros} role="group" aria-label="Filtros del historial de Compras">
        <label>Tipo de movimiento
          <select aria-label="Tipo de movimiento" value={tipo} onChange={(event) => actualizar({ tipo: event.target.value })}>
            {TIPOS.map((opcion) => <option key={opcion.valor || 'todos'} value={opcion.valor}>{opcion.etiqueta}</option>)}
          </select>
        </label>
        <label>Obra
          <select aria-label="Obra" value={obraId} onChange={(event) => actualizar({ obraId: event.target.value })}>
            <option value="">Todas las obras</option>
            {obras.map((obra) => <option key={obra.id} value={obra.id}>{obra.nombre}</option>)}
          </select>
        </label>
        <label>Fecha desde<input aria-label="Fecha desde" type="date" value={desde} onChange={(event) => actualizar({ desde: event.target.value })} /></label>
        <label>Fecha hasta<input aria-label="Fecha hasta" type="date" value={hasta} onChange={(event) => actualizar({ hasta: event.target.value })} /></label>
        <button type="button" onClick={() => { setParams({}, { replace: true }) }} disabled={!hayFiltros}>Quitar filtros</button>
      </div>

      {adjudicacionesSinFecha > 0 && (!tipo || tipo === 'licitaciones') && <p className={styles.aviso} role="status">Se detectaron {adjudicacionesSinFecha} licitación(es) adjudicada(s) en los registros recuperados. No se incluyen como movimientos ubicables cronológicamente porque falta una fecha confiable; el conteo puede no abarcar todo el histórico.</p>}
      {errores.length > 0 && <p className={styles.aviso} role="status">No se pudieron cargar algunos datos ({errores.join(', ')}). El historial muestra las fuentes disponibles.</p>}
      {cargando && <p role="status">Cargando historial…</p>}
      {!cargando && <ListaMovimientos movimientos={movimientosVisibles} nombresObras={nombresObras} />}
      {!cargando && filtrados.length > 0 && <nav className={styles.paginacion} aria-label="Paginación del historial">
        <button type="button" onClick={() => irAPagina(paginaVisible - 1)} disabled={paginaVisible <= 1}>Anterior</button>
        <span>Página {paginaVisible} de {paginas} · {filtrados.length} movimientos</span>
        <button type="button" onClick={() => irAPagina(paginaVisible + 1)} disabled={paginaVisible >= paginas}>Siguiente</button>
      </nav>}
      {!cargando && errores.length > 0 && <button type="button" onClick={() => { setCargando(true); setIntento((n) => n + 1) }}>Reintentar fuentes</button>}
    </section>
  )
}
