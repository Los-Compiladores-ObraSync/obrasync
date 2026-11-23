import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { obtenerMiReputacion } from '../licitaciones/licitacionesService'
import type { ReputacionProveedorDto } from '../licitaciones/licitacionesTipos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import styles from './MiReputacionProveedorPage.module.css'

function formatoCalificacion(valor: number): string {
  return valor.toLocaleString('es-AR', { maximumFractionDigits: 2 })
}

export function MiReputacionProveedorPage() {
  const [reputacion, setReputacion] = useState<ReputacionProveedorDto | null>(null)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState(false)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false
    obtenerMiReputacion()
      .then((datos) => {
        if (!cancelado) {
          setReputacion(datos)
          setError(false)
        }
      })
      .catch(() => {
        if (!cancelado) setError(true)
      })
      .finally(() => {
        if (!cancelado) setCargando(false)
      })
    return () => {
      cancelado = true
    }
  }, [intento])

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>← Volver al inicio</Link>
      <h1>Mi reputación</h1>
      <p className="intro">Consultá las calificaciones y comentarios que recibiste de Compras.</p>

      {cargando && <p role="status">Cargando reputación…</p>}
      {!cargando && error && (
        <div role="alert" className={styles.error}>
          <p>No se pudo cargar tu reputación. Intentá nuevamente.</p>
          <button type="button" onClick={() => { setCargando(true); setIntento((n) => n + 1) }}>Reintentar</button>
        </div>
      )}
      {!cargando && !error && reputacion && (
        <>
          <section className={styles.resumen} aria-label="Resumen de reputación">
            <h2>Tu reputación</h2>
            {reputacion.calificacion == null || reputacion.cantidadCalificaciones === 0 ? (
              <p>Todavía no recibiste calificaciones.</p>
            ) : (
              <p className={styles.promedio}>
                {formatoCalificacion(reputacion.calificacion)} ★ con {reputacion.cantidadCalificaciones}
                {reputacion.cantidadCalificaciones === 1 ? ' calificación' : ' calificaciones'}
              </p>
            )}
          </section>

          <section className={styles.comentarios} aria-label="Últimos comentarios recibidos">
            <h2>Últimos comentarios</h2>
            {reputacion.ultimosComentarios.length === 0 ? (
              <p>Todavía no hay comentarios.</p>
            ) : (
              <ul>
                {reputacion.ultimosComentarios.map((comentario, indice) => (
                  <li key={`${comentario.ordenCompraId ?? 'comentario'}-${comentario.fechaActualizacion}-${indice}`}>
                    <p className={styles.estrellas}>{comentario.calificacion} ★ de 5</p>
                    {comentario.comentario.trim() && <p>{comentario.comentario}</p>}
                    <p className={styles.fecha}>Recibido el {fechaLarga(comentario.fechaActualizacion)}</p>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </section>
  )
}
