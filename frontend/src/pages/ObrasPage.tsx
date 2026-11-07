import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { darDeBajaObra } from '../obras/obrasService'
import { ApiError } from '../api/ApiError'
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

  // Baja de una obra (#119): primero se pide confirmación en pantalla, después se llama al DELETE.
  const [confirmandoId, setConfirmandoId] = useState<string | null>(null)
  const [dandoDeBaja, setDandoDeBaja] = useState(false)
  const bajaEnCurso = useRef(false) // evita doble clic antes de que React deshabilite el botón
  const [mensaje, setMensaje] = useState<{ tipo: 'ok' | 'error'; texto: string } | null>(null)

  async function confirmarBaja(obra: ObraResumen) {
    if (bajaEnCurso.current) return
    bajaEnCurso.current = true
    setDandoDeBaja(true)
    setMensaje(null)
    try {
      await darDeBajaObra(obra.id)
      setObras((actuales) => actuales.filter((o) => o.id !== obra.id))
      setMensaje({ tipo: 'ok', texto: `La obra "${obra.nombre}" se dio de baja.` })
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        // Ya no existe: alguien la dio de baja antes. Se saca de la lista igual.
        setObras((actuales) => actuales.filter((o) => o.id !== obra.id))
        setMensaje({ tipo: 'error', texto: 'Esa obra ya no existe.' })
      } else if (err instanceof ApiError && err.status === 403) {
        setMensaje({ tipo: 'error', texto: 'No tenés permiso para dar de baja obras.' })
      } else {
        setMensaje({ tipo: 'error', texto: 'No se pudo dar de baja la obra. Intentá nuevamente.' })
      }
    } finally {
      setConfirmandoId(null)
      setDandoDeBaja(false)
      bajaEnCurso.current = false
    }
  }

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Obras</h1>

      {mensaje && (
        <p
          className={mensaje.tipo === 'ok' ? styles.exito : styles.errorApi}
          role={mensaje.tipo === 'ok' ? 'status' : 'alert'}
        >
          {mensaje.texto}
        </p>
      )}

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
              <div className={styles.acciones}>
                {confirmandoId === obra.id ? (
                  <>
                    <p className={styles.pregunta}>¿Dar de baja "{obra.nombre}"?</p>
                    <button
                      type="button"
                      className={styles.confirmar}
                      onClick={() => void confirmarBaja(obra)}
                      disabled={dandoDeBaja}
                    >
                      Sí, dar de baja
                    </button>
                    <button
                      type="button"
                      className={styles.accion}
                      onClick={() => setConfirmandoId(null)}
                      disabled={dandoDeBaja}
                    >
                      Cancelar
                    </button>
                  </>
                ) : (
                  <>
                    <Link
                      to={`/compras/obras/${obra.id}/editar`}
                      className={styles.accion}
                      aria-label={`Editar ${obra.nombre}`}
                    >
                      Editar
                    </Link>
                    <button
                      type="button"
                      className={styles.accionPeligro}
                      onClick={() => setConfirmandoId(obra.id)}
                      aria-label={`Dar de baja ${obra.nombre}`}
                    >
                      Dar de baja
                    </button>
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
