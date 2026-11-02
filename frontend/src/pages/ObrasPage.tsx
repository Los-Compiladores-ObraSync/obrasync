import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { ObraResumen } from '../requisiciones/requisicionesTipos'
import styles from './ObrasPage.module.css'

export function ObrasPage() {
  const [obras, setObras] = useState<ObraResumen[]>([])
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const lista = await obtenerObras()
        if (!cancelado) setObras(lista)
      } catch {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar las obras. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) setCargando(false)
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [intento])

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a Gestión y Compras
      </Link>
      <h1>Obras</h1>

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && errorCarga && (
        <div>
          <p className={styles.errorApi} role="alert">
            {errorCarga}
          </p>
          <button type="button" onClick={reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !errorCarga && obras.length === 0 && (
        <div>
          <p>Todavía no hay obras cargadas.</p>
          <Link to="/compras/obras/nueva" className="accion-grande">
            Crear una obra nueva
          </Link>
        </div>
      )}

      {!cargando && !errorCarga && obras.length > 0 && (
        <ul className={styles.lista}>
          {obras.map((obra) => (
            <li key={obra.id} className={styles.tarjeta}>
              <h2 className={styles.nombre}>{obra.nombre}</h2>
              <p className={styles.dato}>Ubicación: {obra.ubicacion}</p>
              <p className={styles.dato}>Capataz: {obra.capataz ?? 'Sin asignar'}</p>
              {obra.etapas.length === 0 ? (
                <p className={styles.dato}>Sin etapas cargadas.</p>
              ) : (
                <ul className={styles.etapas} aria-label={`Etapas de ${obra.nombre}`}>
                  {obra.etapas.map((etapa) => (
                    <li key={etapa.id} className={styles.etapa}>
                      <span>{etapa.nombreEtapa}</span>
                      <span className={styles.estadoEtapa}>{etapa.estado}</span>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
