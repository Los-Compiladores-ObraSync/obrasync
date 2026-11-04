import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { listarLicitaciones } from '../licitaciones/licitacionesService'
import type { LicitacionResponseDto } from '../licitaciones/licitacionesTipos'
import styles from './AprobarRequisicionesPage.module.css'

export function ProveedoresPage() {
  const [licitaciones, setLicitaciones] = useState<LicitacionResponseDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const datos = await listarLicitaciones()
        if (cancelado) return
        setLicitaciones(datos)
      } catch {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar las licitaciones. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) {
          setCargando(false)
        }
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
      <h1>Proveedores</h1>
      <p className="intro">Licitaciones abiertas para cotizar materiales.</p>

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

      {!cargando && !errorCarga && licitaciones.length === 0 && (
        <p>No hay licitaciones abiertas por ahora</p>
      )}

      {!cargando && !errorCarga && licitaciones.length > 0 && (
        <ul className={styles.lista}>
          {licitaciones.map((lic) => {
            const cantItems = lic.items?.length ?? 0
            const textoItems = `${cantItems} ${cantItems === 1 ? 'ítem' : 'ítems'}`
            const fechaLimiteTexto = fechaLarga(lic.fechaLimiteCotizacion)

            return (
              <li key={lic.licitacionId} className={styles.tarjeta}>
                <h2 className={styles.numero}>
                  {lic.numeroRequisicion ?? 'Licitación'}
                </h2>
                <p className={styles.dato}>{textoItems}</p>
                <p className={styles.dato}>
                  Fecha límite: {fechaLimiteTexto || 'Sin fecha límite'}
                </p>
                <div className={styles.acciones}>
                  <Link
                    to={`/proveedores/licitaciones/${lic.licitacionId}`}
                    className={styles.boton}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      textDecoration: 'none',
                    }}
                  >
                    Cotizar
                  </Link>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
