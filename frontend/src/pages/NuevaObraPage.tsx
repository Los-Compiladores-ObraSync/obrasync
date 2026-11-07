import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { esFechaPasada, fechaEnPalabras } from '../obras/fechaEnPalabras'
import { type Capataz, type UsuarioCreado, obtenerCapataces } from '../obras/capatacesService'
import { CrearCapatazForm } from '../obras/CrearCapatazForm'
import { crearObra } from '../obras/obrasService'
import { type PlantillaObra, obtenerPlantillas } from '../obras/plantillasService'
import { type ResultadoUbicacion, validarUbicacion } from '../obras/ubicacionService'
import {
  fechaAIso,
  hayErrores,
  validarObra,
  type ErroresObra,
} from '../obras/validacionObra'
import styles from './NuevaObraPage.module.css'

// Cada etapa lleva un id propio para que React no mezcle los inputs al quitar una del medio.
interface EtapaForm {
  id: number
  nombre: string
}

function mensajeDeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) {
      return err.detail ?? 'Hay datos inválidos. Revisá el formulario.'
    }
    if (err.status === 403) {
      return 'No tenés permiso para crear obras.'
    }
    if (err.status === 409) {
      return err.detail ?? 'Ya existe una obra con esos datos.'
    }
  }
  return 'No se pudo conectar. Intentá de nuevo más tarde.'
}

export function NuevaObraPage() {
  const [nombre, setNombre] = useState('')
  const [ubicacion, setUbicacion] = useState('')
  const [fechaInicio, setFechaInicio] = useState('')
  const [capatazId, setCapatazId] = useState('')
  const [capataces, setCapataces] = useState<Capataz[]>([])
  const [cargandoCapataces, setCargandoCapataces] = useState(true)
  const [errorCapataces, setErrorCapataces] = useState(false)
  const [intentoCapataces, setIntentoCapataces] = useState(0)
  const [creandoCapataz, setCreandoCapataz] = useState(false)

  // Plantillas de obra (#115): son opcionales y solo sirven para llenar las etapas.
  const [plantillas, setPlantillas] = useState<PlantillaObra[]>([])
  const [cargandoPlantillas, setCargandoPlantillas] = useState(true)
  const [errorPlantillas, setErrorPlantillas] = useState(false)
  const [intentoPlantillas, setIntentoPlantillas] = useState(0)
  const [plantillaId, setPlantillaId] = useState('')

  // Validación de la ubicación con OpenStreetMap (#113). Nunca bloquea del todo: si la dirección
  // no se encuentra hace falta una confirmación explícita, y si el servicio falla se crea igual.
  const [estadoUbicacion, setEstadoUbicacion] = useState<ResultadoUbicacion | 'comprobando' | null>(
    null,
  )
  const [ubicacionConfirmada, setUbicacionConfirmada] = useState(false)
  // Texto que se está comprobando: si el usuario lo cambia antes de que llegue la respuesta,
  // esa respuesta ya no corresponde y se descarta.
  const ubicacionVigente = useRef('')
  const [capatazCreado, setCapatazCreado] = useState<string | null>(null)

  const siguienteId = useRef(2)
  const [etapas, setEtapas] = useState<EtapaForm[]>([{ id: 1, nombre: '' }])

  const [errores, setErrores] = useState<ErroresObra>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [creada, setCreada] = useState(false)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato: "enviando" (estado) recién cambia en el próximo render, y dos envíos
  // seguidos (doble clic, o Enter + clic) pueden entrar antes. Un ref cambia al instante.
  const enviandoRef = useRef(false)

  // Cada cambio de `intentoCapataces` vuelve a pedir la lista (primer intento y "Reintentar").
  useEffect(() => {
    let cancelado = false
    obtenerCapataces()
      .then((lista) => {
        if (!cancelado) setCapataces(lista)
      })
      .catch(() => {
        if (!cancelado) setErrorCapataces(true)
      })
      .finally(() => {
        if (!cancelado) setCargandoCapataces(false)
      })
    return () => {
      cancelado = true
    }
  }, [intentoCapataces])

  // Igual que los capataces: cada cambio de `intentoPlantillas` vuelve a pedir la lista.
  // Si falla, la obra se puede crear igual (las plantillas no son obligatorias).
  useEffect(() => {
    let cancelado = false
    obtenerPlantillas()
      .then((lista) => {
        if (!cancelado) setPlantillas(lista)
      })
      .catch(() => {
        if (!cancelado) setErrorPlantillas(true)
      })
      .finally(() => {
        if (!cancelado) setCargandoPlantillas(false)
      })
    return () => {
      cancelado = true
    }
  }, [intentoPlantillas])

  async function comprobarUbicacion(texto: string): Promise<ResultadoUbicacion | null> {
    const direccion = texto.trim()
    if (!direccion) return null
    setEstadoUbicacion('comprobando')
    const resultado = await validarUbicacion(direccion)
    if (ubicacionVigente.current.trim() !== direccion) return null
    setEstadoUbicacion(resultado)
    return resultado
  }

  function cambiarUbicacion(valor: string) {
    ubicacionVigente.current = valor
    setUbicacion(valor)
    setEstadoUbicacion(null)
    setUbicacionConfirmada(false)
  }

  function reintentarPlantillas() {
    setErrorPlantillas(false)
    setCargandoPlantillas(true)
    setIntentoPlantillas((n) => n + 1)
  }

  function elegirPlantilla(id: string) {
    setPlantillaId(id)
    // Cambiar de plantilla (o volver a "Sin plantilla") reemplaza las etapas: las etapas
    // viejas se pierden, pero se pueden editar, agregar o quitar después de elegir.
    setErrores((actuales) => ({ ...actuales, etapas: undefined, etapasItems: undefined }))
    const plantilla = plantillas.find((p) => p.id === id)
    if (!plantilla) {
      siguienteId.current = 2
      setEtapas([{ id: 1, nombre: '' }])
      return
    }
    const ordenadas = [...plantilla.etapas].sort((a, b) => a.orden - b.orden)
    siguienteId.current = ordenadas.length + 1
    setEtapas(ordenadas.map((e, i) => ({ id: i + 1, nombre: e.nombreEtapa })))
  }

  function reintentarCapataces() {
    setErrorCapataces(false)
    setCargandoCapataces(true)
    setIntentoCapataces((n) => n + 1)
  }

  function alCrearCapataz(nuevo: UsuarioCreado) {
    // Se vuelve a pedir la lista (así queda como la ve el servidor) y el nuevo queda elegido.
    setCapatazCreado(nuevo.nombreCompleto)
    setCreandoCapataz(false)
    setCapatazId(nuevo.id)
    reintentarCapataces()
  }

  function agregarEtapa() {
    setEtapas((actuales) => [...actuales, { id: siguienteId.current++, nombre: '' }])
  }

  function quitarEtapa(id: number) {
    setEtapas((actuales) => actuales.filter((e) => e.id !== id))
  }

  function cambiarEtapa(id: number, valor: string) {
    setEtapas((actuales) => actuales.map((e) => (e.id === id ? { ...e, nombre: valor } : e)))
  }

  function limpiarFormulario() {
    setNombre('')
    setUbicacion('')
    setFechaInicio('')
    setCapatazId('')
    setPlantillaId('')
    ubicacionVigente.current = ''
    setEstadoUbicacion(null)
    setUbicacionConfirmada(false)
    siguienteId.current = 2
    setEtapas([{ id: 1, nombre: '' }])
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current) return
    setCreada(false)
    setApiError(null)

    const nombresEtapas = etapas.map((et) => et.nombre)
    const erroresForm = validarObra({ nombre, ubicacion, fechaInicio, capatazId, etapas: nombresEtapas })
    if (hayErrores(erroresForm)) {
      setErrores(erroresForm)
      return
    }
    setErrores({})
    // El guard se activa ANTES de consultar el mapa: esa consulta es asíncrona y, si no, un doble
    // clic podría colar dos envíos mientras se espera la respuesta.
    enviandoRef.current = true
    setEnviando(true)

    try {
      // Un resultado ya conocido sale de la caché: no se repite el pedido que hizo el "blur".
      const ubicacionOk = await comprobarUbicacion(ubicacion)
      if (ubicacionOk === 'no-encontrada' && !ubicacionConfirmada) {
        return // queda el aviso con la casilla de confirmación; no se crea nada todavía
      }
      await crearObra({
        nombre: nombre.trim(),
        ubicacion: ubicacion.trim(),
        fechaInicio: fechaAIso(fechaInicio),
        capatazId: capatazId.trim(),
        fasesEtapas: nombresEtapas.map((n) => n.trim()),
      })
      setCreada(true)
      limpiarFormulario()
    } catch (err) {
      setApiError(mensajeDeError(err))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  function claseInput(conError: boolean) {
    return conError ? `${styles.input} ${styles.inputInvalido}` : styles.input
  }

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Nueva obra</h1>

      <form className={styles.formulario} onSubmit={handleSubmit} noValidate>
        {creada && (
          <p className={styles.exito} role="status">
            Obra creada correctamente.
          </p>
        )}
        {apiError && (
          <p className={styles.errorApi} role="alert">
            {apiError}
          </p>
        )}

        <div className={styles.grupo}>
          <label htmlFor="obra-nombre" className={styles.label}>Nombre de la obra</label>
          <input
            id="obra-nombre"
            className={claseInput(!!errores.nombre)}
            value={nombre}
            onChange={(e) => setNombre(e.target.value)}
            aria-invalid={!!errores.nombre}
            aria-describedby={errores.nombre ? 'obra-nombre-error' : undefined}
          />
          {errores.nombre && (
            <p id="obra-nombre-error" className={styles.errorCampo}>{errores.nombre}</p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="obra-ubicacion" className={styles.label}>Ubicación</label>
          <input
            id="obra-ubicacion"
            className={claseInput(!!errores.ubicacion)}
            value={ubicacion}
            onChange={(e) => cambiarUbicacion(e.target.value)}
            onBlur={() => void comprobarUbicacion(ubicacion)}
            aria-invalid={!!errores.ubicacion}
            aria-describedby={errores.ubicacion ? 'obra-ubicacion-error' : undefined}
          />
          {estadoUbicacion === 'comprobando' && (
            <p className={styles.ayuda} role="status">Comprobando la dirección…</p>
          )}
          {estadoUbicacion === 'encontrada' && (
            <p className={styles.ayuda} role="status">
              Dirección encontrada (datos de © OpenStreetMap).
            </p>
          )}
          {estadoUbicacion === 'no-encontrada' && (
            <div className={styles.aviso} role="status">
              <p>No encontramos esa dirección en el mapa. Revisá que esté bien escrita.</p>
              <label className={styles.confirmacion}>
                <input
                  type="checkbox"
                  checked={ubicacionConfirmada}
                  onChange={(e) => setUbicacionConfirmada(e.target.checked)}
                />
                Es correcta, quiero crear la obra con esta ubicación
              </label>
            </div>
          )}
          {estadoUbicacion === 'no-disponible' && (
            <p className={styles.aviso} role="status">
              No se pudo comprobar la dirección en este momento. Si seguís, la obra se crea igual.
            </p>
          )}
          {errores.ubicacion && (
            <p id="obra-ubicacion-error" className={styles.errorCampo}>{errores.ubicacion}</p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="obra-fecha" className={styles.label}>Fecha de inicio</label>
          <input
            id="obra-fecha"
            type="date"
            className={`${claseInput(!!errores.fechaInicio)} ${styles.inputFecha}`}
            value={fechaInicio}
            onChange={(e) => setFechaInicio(e.target.value)}
            onClick={(e) => {
              // Toda la caja abre el calendario, no solo el iconito.
              try {
                e.currentTarget.showPicker?.()
              } catch {
                /* El navegador no lo permite en este momento: queda el uso normal. */
              }
            }}
            aria-invalid={!!errores.fechaInicio}
            aria-describedby={errores.fechaInicio ? 'obra-fecha-error' : undefined}
          />
          {fechaEnPalabras(fechaInicio) && (
            <p className={styles.ayuda}>Empieza el {fechaEnPalabras(fechaInicio)}.</p>
          )}
          {/* Aviso suave (#127): una obra que ya empezó es un caso válido, así que no bloquea. */}
          {esFechaPasada(fechaInicio) && (
            <p className={styles.aviso} role="status">
              Esta fecha ya pasó. ¿La obra ya empezó? Si es así, está bien.
            </p>
          )}
          {errores.fechaInicio && (
            <p id="obra-fecha-error" className={styles.errorCampo}>{errores.fechaInicio}</p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="obra-capataz" className={styles.label}>Capataz</label>
          <select
            id="obra-capataz"
            className={claseInput(!!errores.capatazId)}
            value={capatazId}
            onChange={(e) => setCapatazId(e.target.value)}
            disabled={cargandoCapataces || errorCapataces}
            aria-invalid={!!errores.capatazId}
            aria-describedby={errores.capatazId ? 'obra-capataz-error' : undefined}
          >
            <option value="">
              {cargandoCapataces ? 'Cargando capataces…' : '— Seleccioná un capataz —'}
            </option>
            {capataces.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nombreCompleto}
              </option>
            ))}
          </select>
          {errorCapataces && (
            <div>
              <p className={styles.errorCampo} role="alert">
                No se pudo cargar la lista de capataces.
              </p>
              <button type="button" className={styles.secundario} onClick={reintentarCapataces}>
                Reintentar
              </button>
            </div>
          )}
          {errores.capatazId && (
            <p id="obra-capataz-error" className={styles.errorCampo}>{errores.capatazId}</p>
          )}
          {capatazCreado && (
            <p className={styles.exito} role="status">
              Capataz {capatazCreado} creado y seleccionado.
            </p>
          )}
          {creandoCapataz ? (
            <CrearCapatazForm
              alCrear={alCrearCapataz}
              alCancelar={() => setCreandoCapataz(false)}
            />
          ) : (
            <button
              type="button"
              className={styles.secundario}
              onClick={() => {
                setCapatazCreado(null)
                setCreandoCapataz(true)
              }}
            >
              + Crear capataz
            </button>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="obra-plantilla" className={styles.label}>
            Empezar desde una plantilla (opcional)
          </label>
          <select
            id="obra-plantilla"
            className={styles.input}
            value={plantillaId}
            onChange={(e) => elegirPlantilla(e.target.value)}
            disabled={cargandoPlantillas || errorPlantillas}
          >
            <option value="">
              {cargandoPlantillas ? 'Cargando plantillas…' : 'Sin plantilla'}
            </option>
            {plantillas.map((p) => (
              <option key={p.id} value={p.id}>
                {p.nombre}
              </option>
            ))}
          </select>
          {plantillas.find((p) => p.id === plantillaId)?.descripcion && (
            <p className={styles.ayuda}>
              {plantillas.find((p) => p.id === plantillaId)?.descripcion} Podés editar las etapas
              antes de crear la obra.
            </p>
          )}
          {errorPlantillas && (
            <div>
              <p className={styles.errorCampo} role="alert">
                No se pudo cargar la lista de plantillas. Podés crear la obra igual.
              </p>
              <button type="button" className={styles.secundario} onClick={reintentarPlantillas}>
                Reintentar plantillas
              </button>
            </div>
          )}
        </div>

        <fieldset className={styles.etapas}>
          <legend className={styles.label}>Etapas de la obra</legend>

          {errores.etapas && <p className={styles.errorCampo}>{errores.etapas}</p>}

          {etapas.map((etapa, i) => {
            const error = errores.etapasItems?.[i]
            return (
              <div key={etapa.id} className={styles.grupo}>
                <div className={styles.filaEtapa}>
                  <input
                    className={claseInput(!!error)}
                    aria-label={`Etapa ${i + 1}`}
                    value={etapa.nombre}
                    onChange={(e) => cambiarEtapa(etapa.id, e.target.value)}
                    aria-invalid={!!error}
                  />
                  <button
                    type="button"
                    className={styles.secundario}
                    aria-label={`Quitar etapa ${i + 1}`}
                    onClick={() => quitarEtapa(etapa.id)}
                  >
                    Quitar
                  </button>
                </div>
                {error && <p className={styles.errorCampo}>{error}</p>}
              </div>
            )
          })}

          <button type="button" className={styles.secundario} onClick={agregarEtapa}>
            + Agregar etapa
          </button>
        </fieldset>

        <button type="submit" className={styles.boton} disabled={enviando}>
          {enviando ? 'Creando…' : 'Crear obra'}
        </button>
      </form>
    </section>
  )
}
