import { aceptaTextoNumerico, esPresentacionEntera } from '../formularios/numeros'
import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { useAuth } from '../auth/useAuth'
import {
  corregirCotizacion,
  crearCotizacion,
  obtenerLicitacion,
  obtenerMisCotizaciones,
} from '../licitaciones/licitacionesService'
import { formatoPrecio } from '../licitaciones/cotizacionesCalculos'
import { PlazoEntregaOpciones } from '../licitaciones/PlazoEntregaOpciones'
import type {
  CotizacionResponseDto,
  CrearCotizacionRequestDto,
  LicitacionResponseDto,
} from '../licitaciones/licitacionesTipos'
import {
  hayErroresCotizacion,
  parsearNumeroDecimal,
  validarCantidadOfrecida,
  validarCotizacion,
  validarFlete,
  validarPlazoEntregaDias,
  validarPrecioUnitario,
  validarPresentacionOfrecida,
  type ErroresCotizacion,
  type OtraPresentacionFormulario,
} from '../licitaciones/validacionCotizacion'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './CotizarLicitacionPage.module.css'

export function CotizarLicitacionPage() {
  const { usuario } = useAuth()
  if (usuario?.rol !== 'Proveedor') {
    return (
      <section className={styles.contenedor}>
        <div className={styles.tarjetaAviso} role="alert">
          <h2>Sin acceso</h2>
          <p>Solo un proveedor puede cotizar esta licitación.</p>
          <Link to="/">Volver al inicio</Link>
        </div>
      </section>
    )
  }
  return <FormularioCotizacion />
}

function FormularioCotizacion() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useAuth()
  // ?corregir=<cotizacionId>: el Proveedor corrige una oferta que ya envió (antes del cierre).
  const [busqueda] = useSearchParams()
  const cotizacionACorregir = busqueda.get('corregir')

  const [licitacion, setLicitacion] = useState<LicitacionResponseDto | null>(null)
  const [cargandoLicitacion, setCargandoLicitacion] = useState(true)
  const [error404, setError404] = useState(false)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)

  const [precios, setPrecios] = useState<Record<string, string>>({})
  const [flete, setFlete] = useState('')
  const [plazoEntregaDias, setPlazoEntregaDias] = useState('')

  // Ítems donde el Proveedor activó "Ofrecer otra presentación" (#229). Sin entrada = cotiza lo pedido.
  const [otras, setOtras] = useState<Record<string, OtraPresentacionFormulario | undefined>>({})
  // Ítems de una oferta anterior que el servicio marcó con cobertura parcial (solo al corregir).
  const [parcialesPrevios, setParcialesPrevios] = useState<Record<string, boolean>>({})
  // Lo que respondió el servicio al enviar: de ahí sale el aviso de cobertura parcial.
  const [respuestaEnviada, setRespuestaEnviada] = useState<CotizacionResponseDto | null>(null)

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
        if (cotizacionACorregir) {
          const mias = await obtenerMisCotizaciones()
          if (cancelado) return
          const previa = mias.find((c) => c.cotizacionId === cotizacionACorregir)
          if (previa) {
            setFlete(String(previa.flete))
            setPlazoEntregaDias(String(previa.plazoEntregaDias))
            setPrecios(Object.fromEntries(previa.items.map((i) => [i.articuloId, String(i.precioUnitario)])))
            setOtras(
              Object.fromEntries(
                previa.items
                  .filter((i) => i.presentacionOfrecida || i.cantidadOfrecida != null)
                  .map((i) => [
                    i.articuloId,
                    {
                      presentacion: i.presentacionOfrecida ?? '',
                      cantidad: i.cantidadOfrecida != null ? String(i.cantidadOfrecida) : '',
                    },
                  ]),
              ),
            )
            setParcialesPrevios(
              Object.fromEntries(previa.items.filter((i) => i.coberturaParcial).map((i) => [i.articuloId, true])),
            )
          }
        }
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
  }, [id, intento, cotizacionACorregir])

  function activarOtraPresentacion(articuloId: string, activa: boolean) {
    setOtras((prev) => {
      const siguiente = { ...prev }
      if (activa) siguiente[articuloId] = { presentacion: '', cantidad: '' }
      else delete siguiente[articuloId]
      return siguiente
    })
    if (!activa) {
      // Al desactivarla se descartan sus errores: ese ítem vuelve a cotizarse como lo pedido.
      setErrores((prev) => ({
        ...prev,
        otrasPresentacionesPorArticulo: { ...prev.otrasPresentacionesPorArticulo, [articuloId]: undefined },
      }))
    }
  }

  /** ¿Lo que ofrece en este ítem se cuenta en enteros (bolsas)? Depende del material pedido y de la presentación escrita. */
  function ofertaEntera(articuloId: string, presentacion: string) {
    const unidad = licitacion?.items.find((it) => it.articuloId === articuloId)?.unidadMedida
    return esPresentacionEntera(presentacion, unidad)
  }

  function validarCampoOtra(articuloId: string, campo: 'presentacion' | 'cantidad', valor: string) {
    const error =
      campo === 'presentacion'
        ? validarPresentacionOfrecida(valor)
        : validarCantidadOfrecida(valor, ofertaEntera(articuloId, otras[articuloId]?.presentacion ?? ''))
    setErrores((prev) => ({
      ...prev,
      otrasPresentacionesPorArticulo: {
        ...prev.otrasPresentacionesPorArticulo,
        [articuloId]: { ...prev.otrasPresentacionesPorArticulo?.[articuloId], [campo]: error },
      },
    }))
  }

  function cambiarCampoOtra(articuloId: string, campo: 'presentacion' | 'cantidad', valor: string) {
    // La cantidad nunca lleva coma, y si la presentación se cuenta en bolsas tampoco lleva decimales.
    if (campo === 'cantidad' && !aceptaTextoNumerico(valor, ofertaEntera(articuloId, otras[articuloId]?.presentacion ?? ''))) return
    setOtras((prev) => ({
      ...prev,
      [articuloId]: {
        presentacion: prev[articuloId]?.presentacion ?? '',
        cantidad: prev[articuloId]?.cantidad ?? '',
        [campo]: valor,
      },
    }))
    if (tocados[`${campo}-${articuloId}`]) validarCampoOtra(articuloId, campo, valor)
  }

  function salirDeCampoOtra(articuloId: string, campo: 'presentacion' | 'cantidad') {
    setTocados((prev) => ({ ...prev, [`${campo}-${articuloId}`]: true }))
    validarCampoOtra(articuloId, campo, otras[articuloId]?.[campo] ?? '')
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()

    if (!licitacion) return

    const articulosEsperados = licitacion.items.map((it) => it.articuloId)
    const nuevosErrores = validarCotizacion(
      { flete, plazoEntregaDias, preciosPorArticulo: precios, otrasPresentacionesPorArticulo: otras },
      articulosEsperados,
      Object.fromEntries(licitacion.items.map((it) => [it.articuloId, it.unidadMedida])),
    )

    const todosTocados: Record<string, boolean> = {
      flete: true,
      plazoEntregaDias: true,
    }
    for (const artId of articulosEsperados) {
      todosTocados[artId] = true
      if (otras[artId]) {
        todosTocados[`presentacion-${artId}`] = true
        todosTocados[`cantidad-${artId}`] = true
      }
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
      items: licitacion.items.map((it) => {
        const otra = otras[it.articuloId]
        return {
          articuloId: it.articuloId,
          precioUnitario: parsearNumeroDecimal(precios[it.articuloId] ?? '') ?? 0,
          // Solo se mandan los campos de la otra presentación si el Proveedor la activó en este ítem.
          ...(otra
            ? {
                presentacionOfrecida: otra.presentacion.trim(),
                cantidadOfrecida: parsearNumeroDecimal(otra.cantidad) ?? undefined,
              }
            : {}),
        }
      }),
    }

    try {
      const respuesta = cotizacionACorregir
        ? await corregirCotizacion(cotizacionACorregir, dto)
        : await crearCotizacion(dto)
      setRespuestaEnviada(respuesta)
      setEnviadoExitoso(true)
    } catch (err) {
      let mensaje = 'Ocurrió un error al enviar la cotización. Intentá nuevamente.'
      let bloqueante = false

      if (err instanceof ApiError) {
        mensaje = err.status === 403
          ? 'No tenés permiso para enviar o corregir cotizaciones. Solo los proveedores pueden realizar esta acción.'
          : err.detail || mensaje
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
    // La marca la calcula el servicio (#190): acá solo se muestra lo que devolvió, sin calcular equivalencias.
    const itemsParciales = licitacion.items.filter(
      (it) => respuestaEnviada?.items?.find((i) => i.articuloId === it.articuloId)?.coberturaParcial,
    )
    const hayCoberturaParcial = Boolean(respuestaEnviada?.coberturaParcial) || itemsParciales.length > 0

    return (
      <section className={styles.contenedor}>
        <div className={styles.tarjetaConfirmacion} role="status">
          <h2 className={styles.tituloExito}>
            {cotizacionACorregir ? '¡Cotización corregida con éxito!' : '¡Cotización enviada con éxito!'}
          </h2>
          <p>
            Tu cotización para la licitación{' '}
            <strong>{licitacion.numeroRequisicion ?? licitacion.licitacionId}</strong>{' '}
            {cotizacionACorregir ? 'fue actualizada correctamente.' : 'fue registrada correctamente.'}
          </p>
          {hayCoberturaParcial && (
            <div className={styles.avisoCobertura} role="alert">
              <strong>Esto no cubre todo lo pedido.</strong>
              <p>
                Tu oferta se envió igual: Compras la ve marcada y decide al adjudicar.
              </p>
              {itemsParciales.length > 0 && (
                <ul>
                  {itemsParciales.map((it) => (
                    <li key={it.articuloId}>{it.descripcion}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
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
      <h1>
        {cotizacionACorregir ? 'Corregir cotización de' : 'Cotizar'} {licitacion.numeroRequisicion ?? 'licitación'}
      </h1>

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
            const otra = otras[it.articuloId]
            const erroresOtra = errores.otrasPresentacionesPorArticulo?.[it.articuloId]
            const errorPresentacion = tocados[`presentacion-${it.articuloId}`] ? erroresOtra?.presentacion : undefined
            const errorCantidad = tocados[`cantidad-${it.articuloId}`] ? erroresOtra?.cantidad : undefined
            const cantidadOfrecida = otra ? parsearNumeroDecimal(otra.cantidad) : null
            const precioOfrecido = parsearNumeroDecimal(precios[it.articuloId] ?? '')
            // Mismo cálculo que el contrato (#190): cantidad ofrecida × precio por unidad ofrecida.
            const totalOfrecido =
              cantidadOfrecida !== null && cantidadOfrecida > 0 && precioOfrecido !== null && precioOfrecido > 0
                ? Math.round(cantidadOfrecida * precioOfrecido * 100) / 100
                : null

            return (
              <div key={it.articuloId} className={styles.filaItem}>
                <div className={styles.infoItem}>
                  <strong className={styles.descripcionItem}>{it.descripcion}</strong>
                  <span className={styles.cantidadItem}>Cantidad: {unidadTexto}</span>
                </div>

                <button
                  type="button"
                  className={styles.botonOtraPresentacion}
                  aria-expanded={Boolean(otra)}
                  onClick={() => activarOtraPresentacion(it.articuloId, !otra)}
                  disabled={enviando || esBloqueante}
                >
                  {otra ? 'Cancelar' : 'Ofrecer otra presentación'}
                </button>

                {otra && (
                  <div className={styles.bloqueOtraPresentacion}>
                    <div className={styles.grupo}>
                      <label htmlFor={`presentacion-${it.articuloId}`} className={styles.label}>
                        Presentación
                      </label>
                      <p className={styles.ayudaCampo}>
                        Escribí qué ofrecés en lugar de lo pedido. Por ejemplo: bolsa de 25 kg.
                      </p>
                      <input
                        id={`presentacion-${it.articuloId}`}
                        type="text"
                        className={
                          errorPresentacion ? `${styles.input} ${styles.inputInvalido}` : styles.input
                        }
                        value={otra.presentacion}
                        onChange={(e) => cambiarCampoOtra(it.articuloId, 'presentacion', e.target.value)}
                        onBlur={() => salirDeCampoOtra(it.articuloId, 'presentacion')}
                        placeholder="Ej: bolsa de 25 kg"
                        disabled={enviando || esBloqueante}
                      />
                      {errorPresentacion && (
                        <p className={styles.errorCampo} role="alert">
                          {errorPresentacion}
                        </p>
                      )}
                    </div>
                    <div className={styles.grupo}>
                      <label htmlFor={`cantidad-${it.articuloId}`} className={styles.label}>
                        Cantidad de unidades
                      </label>
                      <input
                        id={`cantidad-${it.articuloId}`}
                        type="text"
                        inputMode="decimal"
                        className={errorCantidad ? `${styles.input} ${styles.inputInvalido}` : styles.input}
                        value={otra.cantidad}
                        onChange={(e) => cambiarCampoOtra(it.articuloId, 'cantidad', e.target.value)}
                        onBlur={() => salirDeCampoOtra(it.articuloId, 'cantidad')}
                        placeholder="Ej: 2"
                        disabled={enviando || esBloqueante}
                      />
                      {errorCantidad && (
                        <p className={styles.errorCampo} role="alert">
                          {errorCantidad}
                        </p>
                      )}
                    </div>
                  </div>
                )}

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
                      if (!aceptaTextoNumerico(val)) return
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
                    placeholder="0.00"
                    disabled={enviando || esBloqueante}
                  />
                  {errorItem && (
                    <p className={styles.errorCampo} role="alert">
                      {errorItem}
                    </p>
                  )}
                </div>

                {otra && totalOfrecido !== null && (
                  <p className={styles.resumenOferta} role="status">
                    Total que ofrecés: <strong>{formatoPrecio(totalOfrecido)}</strong> · Pedido: {unidadTexto}
                  </p>
                )}

                {parcialesPrevios[it.articuloId] && (
                  <p className={styles.avisoParcialItem} role="alert">
                    Esto no cubre todo lo pedido.
                  </p>
                )}
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
              if (!aceptaTextoNumerico(val)) return
              setFlete(val)
              if (tocados.flete) {
                setErrores((prev) => ({ ...prev, flete: validarFlete(val) }))
              }
            }}
            onBlur={() => {
              setTocados((prev) => ({ ...prev, flete: true }))
              setErrores((prev) => ({ ...prev, flete: validarFlete(flete) }))
            }}
            placeholder="0.00"
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
            Plazo de entrega (en días)
          </label>
          <p className={styles.ayudaCampo}>
            Elegí un plazo frecuente o escribí otro: solo el número de días corridos, hasta 15. Por ejemplo, 5 significa cinco días.
          </p>
          <PlazoEntregaOpciones
            valor={plazoEntregaDias}
            deshabilitado={enviando || esBloqueante}
            onElegir={(dias) => {
              const val = String(dias)
              setPlazoEntregaDias(val)
              setErrores((prev) => ({ ...prev, plazoEntregaDias: validarPlazoEntregaDias(val) }))
            }}
          />
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
              if (!aceptaTextoNumerico(val)) return
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
          {enviando ? 'Enviando…' : cotizacionACorregir ? 'Guardar cambios' : 'Enviar cotización'}
        </button>
      </form>
    </section>
  )
}
