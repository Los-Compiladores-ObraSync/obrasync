import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { MAX_PENDIENTES_EN_TABLERO } from '../notificaciones/configuracionAvisos'
import { usePendientes } from '../notificaciones/usePendientes'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
import styles from './PendientesPage.module.css'

/** Tablero de pendientes del Comprador (#123): lo que espera su aprobación, lo más viejo primero.
 *  La lista sale del PendientesProvider (la misma que alimenta la campana), así no se pide dos veces. */
export function PendientesPage() {
  const { cantidad, pendientes, cargado, huboError, refrescar } = usePendientes()
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})

  useEffect(() => {
    let cancelado = false
    // Si falla la lista de obras no es grave: se muestra el identificador.
    obtenerObras()
      .then((obras) => {
        if (!cancelado) setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
      })
      .catch(() => {})
    return () => {
      cancelado = true
    }
  }, [])

  // Se ordena por fecha real (no como texto: "…12:00:00.5Z" y "…12:00:00Z" no se comparan bien
  // como string). Sort sobre una copia para no tocar la lista del proveedor.
  const masAntiguas = [...pendientes]
    .sort((a, b) => Date.parse(a.fechaCreacion) - Date.parse(b.fechaCreacion))
    .slice(0, MAX_PENDIENTES_EN_TABLERO)

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a Gestión y Compras
      </Link>
      <h1>Pendientes</h1>

      {!cargado && !huboError && <p role="status">Cargando…</p>}

      {!cargado && huboError && (
        <div>
          <p className={styles.errorApi} role="alert">
            No se pudieron cargar los pendientes. Intentá nuevamente.
          </p>
          <button type="button" onClick={refrescar}>
            Reintentar
          </button>
        </div>
      )}

      {cargado && (
        <section className={styles.tarjeta} aria-labelledby="titulo-por-aprobar">
          <div className={styles.encabezado}>
            <h2 id="titulo-por-aprobar" className={styles.titulo}>
              Requisiciones por aprobar
            </h2>
            <span className={styles.cantidad}>{cantidad}</span>
          </div>

          {cantidad === 0 ? (
            <div role="status">
              <p className={styles.alDia}>Estás al día</p>
              <p className={styles.dato}>No hay requisiciones esperando tu aprobación.</p>
            </div>
          ) : (
            <>
              <ul className={styles.lista} aria-label="Requisiciones más antiguas">
                {masAntiguas.map((req) => (
                  <li key={req.requisicionId} className={styles.fila}>
                    <span className={styles.numero}>{req.numeroRequisicion}</span>
                    <span className={styles.dato}>
                      Obra: {nombresObras[req.obraId] ?? req.obraId}
                      {req.etapa ? ` · Etapa: ${req.etapa}` : ''}
                    </span>
                    <span className={styles.dato}>Creada el {fechaLarga(req.fechaCreacion)}</span>
                  </li>
                ))}
              </ul>
              {cantidad > masAntiguas.length && (
                <p className={styles.dato}>
                  Mostrando las {masAntiguas.length} más antiguas de {cantidad}.
                </p>
              )}
              <Link to="/compras/requisiciones" className="accion-grande">
                Ir a Aprobar requisiciones
              </Link>
            </>
          )}
        </section>
      )}
    </section>
  )
}
