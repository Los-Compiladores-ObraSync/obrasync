import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import styles from '../pages/AprobarRequisicionesPage.module.css'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { listarLicitaciones } from './licitacionesService'
import type { LicitacionResponseDto } from './licitacionesTipos'

interface Props {
  /** Etiqueta del título de cada tarjeta: h2 en su propia pantalla, h3 cuando va dentro del Inicio
   *  (debajo de otro título). */
  tituloTarjeta?: 'h2' | 'h3'
}

/** Licitaciones abiertas para cotizar, con su botón "Cotizar". La usan la pantalla de Proveedores
 *  y el Inicio del Proveedor (así llega a cotizar con un solo toque). */
export function ListaLicitaciones({ tituloTarjeta = 'h2' }: Props) {
  const [licitaciones, setLicitaciones] = useState<LicitacionResponseDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)
  const Titulo = tituloTarjeta

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
    <>
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
                <Titulo className={styles.numero}>{lic.numeroRequisicion ?? 'Licitación'}</Titulo>
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
    </>
  )
}
