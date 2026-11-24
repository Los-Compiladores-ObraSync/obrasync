import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fechaDeRecepcion } from '../recepciones/fechasRecepcion'
import { obtenerHistorialRecepciones } from '../recepciones/recepcionesService'
import type { RecepcionHistorial } from '../recepciones/recepcionesTipos'
import { SeguimientoEntrega } from '../recepciones/SeguimientoEntrega'
import { obtenerObras } from '../requisiciones/requisicionesService'
import styles from './RecepcionPage.module.css'

const FILTROS = [
  { valor: 'todas', etiqueta: 'Todas' },
  { valor: 'completas', etiqueta: 'Completas' },
  { valor: 'pendientes', etiqueta: 'Pendientes' },
  { valor: 'parciales', etiqueta: 'Parciales' },
  { valor: 'problemas', etiqueta: 'Con problemas' },
] as const

type Filtro = typeof FILTROS[number]['valor']

export function HistorialRecepcionesPage() {
  const [filtro, setFiltro] = useState<Filtro>('todas')
  const [recepciones, setRecepciones] = useState<RecepcionHistorial[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState(false)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false
    void obtenerHistorialRecepciones()
      .then((lista) => { if (!cancelado) setRecepciones(lista) })
      .catch(() => { if (!cancelado) setError(true) })
      .finally(() => { if (!cancelado) setCargando(false) })
    void obtenerObras()
      .then((obras) => {
        if (!cancelado) setNombresObras(Object.fromEntries(obras.map((obra) => [obra.id, obra.nombre])))
      })
      .catch(() => {})
    return () => { cancelado = true }
  }, [intento])

  const ordenes = new Map<string, RecepcionHistorial>()
  const recientes = [...recepciones].sort((a, b) =>
    (b.createdAt ?? b.fechaRecepcion ?? '').localeCompare(a.createdAt ?? a.fechaRecepcion ?? ''),
  )
  for (const recepcion of recientes) {
    const ordenId = recepcion.ordenCompraId
    if (ordenId && !ordenes.has(ordenId)) ordenes.set(ordenId, recepcion)
  }

  const visibles = [...ordenes].filter(([, recepcion]) => {
    switch (filtro) {
      case 'completas': return recepcion.estado === 3
      case 'pendientes': return recepcion.estado !== 3
      case 'parciales': return recepcion.estado === 2
      case 'problemas': return recepcion.estado === 4
      default: return true
    }
  })

  return (
    <section className={styles.contenedor}>
      <Link to="/obra/recepcion" className={styles.volverEnlace}>← Volver a Recepción</Link>
      <h1>Historial de recepciones</h1>
      <div className={styles.filtrosHistorial} role="group" aria-label="Estado de recepción">
        {FILTROS.map(({ valor, etiqueta }) => (
          <button key={valor} type="button" aria-pressed={filtro === valor} onClick={() => setFiltro(valor)}>
            {etiqueta}
          </button>
        ))}
      </div>
      {cargando && <p role="status">Cargando historial…</p>}
      {error && (
        <>
          <p role="alert">No se pudo cargar el historial de recepciones.</p>
          <button type="button" className={styles.boton} onClick={() => {
            setError(false)
            setCargando(true)
            setIntento((actual) => actual + 1)
          }}>Reintentar</button>
        </>
      )}
      {!cargando && !error && ordenes.size === 0 && <p>Todavía no hay recepciones registradas.</p>}
      {!cargando && !error && ordenes.size > 0 && visibles.length === 0 && (
        <p role="status">No hay órdenes para este filtro.</p>
      )}
      {visibles.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map(([ordenId, recepcion]) => {
            const codigo = ordenId.slice(0, 8).toUpperCase()
            const parcial = recepcion.estado === 2
            const problema = recepcion.estado === 4
            return (
              <li key={ordenId} className={`${styles.tarjeta} ${parcial || problema ? styles.tarjetaIncidencia : ''}`}>
                <h2 className={styles.subtitulo}>Orden {codigo}</h2>
                {recepcion.obraId && nombresObras[recepcion.obraId] && (
                  <p className={styles.dato}>{nombresObras[recepcion.obraId]}</p>
                )}
                <p className={styles.dato}>Último registro: {fechaDeRecepcion(recepcion.fechaRecepcion)}</p>
                {parcial && <p className={styles.avisoProblema}>Recepción parcial</p>}
                {problema && <p className={styles.avisoProblema}>Entrega con problema{recepcion.motivoProblema ? `: ${recepcion.motivoProblema}` : ''}</p>}
                {recepcion.estado === 3 && <p className={styles.dato}><strong>Recepción completa</strong></p>}
                <SeguimientoEntrega ordenCompraId={ordenId} codigo={codigo} />
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
