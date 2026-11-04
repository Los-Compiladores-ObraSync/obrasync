import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { useAuth } from '../auth/useAuth'
import { crearCotizacion, obtenerLicitacion } from '../licitaciones/licitacionesService'
import type { CrearCotizacionRequestDto, LicitacionResponseDto } from '../licitaciones/licitacionesTipos'
import {
  hayErroresCotizacion,
  parsearNumeroDecimal,
  validarCotizacion,
  validarFlete,
  validarPlazoEntregaDias,
  validarPrecioUnitario,
  type ErroresCotizacion,
} from '../licitaciones/validacionCotizacion'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './CotizarLicitacionPage.module.css'

export function CotizarLicitacionPage() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useAuth()

  const [licitacion, setLicitacion] = useState<LicitacionResponseDto | null>(null)
  const [cargandoLicitacion, setCargandoLicitacion] = useState(true)
  const [error404, setError404] = useState(false)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)

  const [precios, setPrecios] = useState<Record<string, string>>({})
  const [flete, setFlete] = useState('')
  const [plazoEntregaDias, setPlazoEntregaDias] = useState('')

  const [tocados, setTocados] = useState<Record<string, boolean>>({})
  const [errores, setErrores] = useState<ErroresCotizacion>({})

  const [enviando, setEnviando] = useState(false)
  const [enviadoExitoso, setEnviadoExitoso] = useState(false)
  const [errorServidor, setErrorServidor] = useState<string | null>(null)
  const [esBloqueante, setEsBloqueante] = useState(false)

  const peticionEnVueloRef = useRef(false)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      if (!id) {
        if (!cancelado) {
          setError404(true)
          setCargandoLicitacion(false)
        }
        return
      }

      try {
        const datos = await obtenerLicitacion(id)
        if (cancelado) return
        setLicitacion(datos)
      } catch (err) {
        if (cancelado) return
        if (err instanceof ApiError && err.status === 404) {
          setError404(true)
        } else {
          setErrorCarga(
            err instanceof ApiError && err.detail
              ? err.detail
              : 'No se pudo cargar la licitación. Intentá nuevamente.',
          )
        }
      } finally {
        if (!cancelado) {
          setCargandoLicitacion(false)
        }
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [id, intento])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()

    if (!licitacion) return

    const articulosEsperados = licitacion.items.map((it) => it.articuloId)
    const nuevosErrores = validarCotizacion(
      { flete, plazoEntregaDias, preciosPorArticulo: precios },
      articulosEsperados,
    )

    const todosTocados: Record<string, boolean> = {
      flete: true,
      plazoEntregaDias: true,
    }
    for (const artId of articulosEsperados) {
      todosTocados[artId] = true
    }
    setTocados(todosTocados)
    setErrores(nuevosErrores)

    if (hayErroresCotizacion(nuevosErrores)) {
      return
    }

    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setEnviando(true)
    setErrorServidor(null)

    const dto: CrearCotizacionRequestDto = {
      licitacionId: licitacion.licitacionId,
      proveedorId: usuario?.id ?? '',
      flete: parsearNumeroDecimal(flete) ?? 0,
      plazoEntregaDias: Number(plazoEntregaDias.trim()),
      items: licitacion.items.map((it) => ({
        articuloId: it.articuloId,
        precioUnitario: parsearNumeroDecimal(precios[it.articuloId] ?? '') ?? 0,
      })),
    }

    try {
      await crearCotizacion(dto)
      setEnviadoExitoso(true)
    } catch (err) {
      let mensaje = 'Ocurrió un error al enviar la cotización. Intentá nuevamente.'
      let bloqueante = false

      if (err instanceof ApiError) {
        mensaje = err.detail || mensaje
        const detailMin = (err.detail ?? '').toLowerCase()
        if (
          err.status === 409 ||
          detailMin.includes('cerrada') ||
          detailMin.includes('ya cotiz') ||
          detailMin.includes('ya presentó')
        ) {
          bloqueante = true
        }
      }
      setErrorServidor(mensaje)
      setEsBloqueante(bloqueante)
    } finally {
      peticionEnVueloRef.current = false
      setEnviando(false)
    }
  }

  function reintentar() {
    setErrorCarga(null)
    setCargandoLicitacion(true)
    setIntento((n) => n + 1)
  }

  if (cargandoLicitacion) {
    return (
      <section className={styles.contenedor}>
        <p role="status">Cargando…</p>
      </section>
    )
  }

  if (error404) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.tarjetaAviso} role="alert">
          <h2>La licitación ya no existe</h2>
          <p>El pliego que buscás no se encuentra disponible o fue eliminado.</p>
          <Link to="/proveedores" className={styles.enlaceVolver}>
            Volver a licitaciones
          </Link>
        </div>
      </section>
    )
  }

  if (errorCarga) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.errorApi} role="alert">
          <p>{errorCarga}</p>
          <button type="button" onClick={reintentar}>
            Reintentar
          </button>
        </div>
      </section>
    )
  }

  if (!licitacion) {
    return null
  }

  if (enviadoExitoso) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.tarjetaConfirmacion} role="status">
          <h2 className={styles.tituloExito}>¡Cotización enviada con éxito!</h2>
          <p>
            Tu cotización para la licitación{' '}
            <strong>{licitacion.numeroRequisicion ?? licitacion.licitacionId}</strong> fue
            registrada correctamente.
          </p>
          <Link to="/proveedores" className={styles.enlaceVolver}>
            Volver a licitaciones
          </Link>
        </div>
      </section>
    )
  }

  return (
    <section className={styles.contenedor}>
      <Link to="/proveedores" className={styles.volver}>
        ← Volver a licitaciones
      </Link>
      <h1>Cotizar {licitacion.numeroRequisicion ?? 'licitación'}</h1>

      {errorServidor && (
        <div className={styles.errorApi} role="alert">
          <p>{errorServidor}</p>
          {esBloqueante && (
            <Link to="/proveedores" className={styles.enlaceVolver}>
              Volver a licitaciones
            </Link>
          )}
        </div>
      )}

      <form onSubmit={(e) => void handleSubmit(e)} className={styles.formulario} noValidate>
        <fieldset className={styles.itemsFieldset}>
          <legend className={styles.itemsLegend}>Materiales requeridos</legend>
          {licitacion.items.map((it) => {
            const errorItem = tocados[it.articuloId]
              ? errores.preciosPorArticulo?.[it.articuloId]
              : undefined
            const unidadTexto = cantidadConUnidad(it.cantidad, it.unidadMedida)

            return (
              <div key={it.articuloId} className={styles.filaItem}>
                <div className={styles.infoItem}>
                  <strong className={styles.descripcionItem}>{it.descripcion}</strong>
                  <span className={styles.cantidadItem}>Cantidad: {unidadTexto}</span>
                </div>
                <div className={styles.grupo}>
                  <label htmlFor={`precio-${it.articuloId}`} className={styles.label}>
                    Precio unitario ($)
                  </label>
                  <input
                    id={`precio-${it.articuloId}`}
                    type="text"
                    inputMode="decimal"
                    className={
                      errorItem ? `${styles.input} ${styles.inputInvalido}` : styles.input
                    }
                    value={precios[it.articuloId] ?? ''}
                    onChange={(e) => {
                      const val = e.target.value
                      setPrecios((prev) => ({ ...prev, [it.articuloId]: val }))
                      if (tocados[it.articuloId]) {
                        const err = validarPrecioUnitario(val)
                        setErrores((prev) => ({
                          ...prev,
                          preciosPorArticulo: {
                            ...prev.preciosPorArticulo,
                            [it.articuloId]: err,
                          },
                        }))
                      }
                    }}
                    onBlur={() => {
                      setTocados((prev) => ({ ...prev, [it.articuloId]: true }))
                      const err = validarPrecioUnitario(precios[it.articuloId] ?? '')
                      setErrores((prev) => ({
                        ...prev,
                        preciosPorArticulo: {
                          ...prev.preciosPorArticulo,
                          [it.articuloId]: err,
                        },
                      }))
                    }}
                    placeholder="0,00"
                    disabled={enviando || esBloqueante}
                  />
                  {errorItem && (
                    <p className={styles.errorCampo} role="alert">
                      {errorItem}
                    </p>
                  )}
                </div>
              </div>
            )
          })}
        </fieldset>

        <div className={styles.grupo}>
          <label htmlFor="flete" className={styles.label}>
            Costo de flete ($)
          </label>
          <input
            id="flete"
            type="text"
            inputMode="decimal"
            className={
              tocados.flete && errores.flete
                ? `${styles.input} ${styles.inputInvalido}`
                : styles.input
            }
            value={flete}
            onChange={(e) => {
              const val = e.target.value
              setFlete(val)
              if (tocados.flete) {
                setErrores((prev) => ({ ...prev, flete: validarFlete(val) }))
              }
            }}
            onBlur={() => {
              setTocados((prev) => ({ ...prev, flete: true }))
              setErrores((prev) => ({ ...prev, flete: validarFlete(flete) }))
            }}
            placeholder="0,00"
            disabled={enviando || esBloqueante}
          />
          {tocados.flete && errores.flete && (
            <p className={styles.errorCampo} role="alert">
              {errores.flete}
            </p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="plazoEntregaDias" className={styles.label}>
            Plazo de entrega (días)
          </label>
          <input
            id="plazoEntregaDias"
            type="text"
            inputMode="numeric"
            className={
              tocados.plazoEntregaDias && errores.plazoEntregaDias
                ? `${styles.input} ${styles.inputInvalido}`
                : styles.input
            }
            value={plazoEntregaDias}
            onChange={(e) => {
              const val = e.target.value
              setPlazoEntregaDias(val)
              if (tocados.plazoEntregaDias) {
                setErrores((prev) => ({
                  ...prev,
                  plazoEntregaDias: validarPlazoEntregaDias(val),
                }))
              }
            }}
            onBlur={() => {
              setTocados((prev) => ({ ...prev, plazoEntregaDias: true }))
              setErrores((prev) => ({
                ...prev,
                plazoEntregaDias: validarPlazoEntregaDias(plazoEntregaDias),
              }))
            }}
            placeholder="Ej: 7"
            disabled={enviando || esBloqueante}
          />
          {tocados.plazoEntregaDias && errores.plazoEntregaDias && (
            <p className={styles.errorCampo} role="alert">
              {errores.plazoEntregaDias}
            </p>
          )}
        </div>

        <button
          type="submit"
          className={styles.botonEnviar}
          disabled={enviando || esBloqueante}
        >
          {enviando ? 'Enviando…' : 'Enviar cotización'}
        </button>
      </form>
    </section>
  )
}
