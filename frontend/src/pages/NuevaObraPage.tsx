import { type FormEvent, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { fechaEnPalabras } from '../obras/fechaEnPalabras'
import { crearObra } from '../obras/obrasService'
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

  const siguienteId = useRef(2)
  const [etapas, setEtapas] = useState<EtapaForm[]>([{ id: 1, nombre: '' }])

  const [errores, setErrores] = useState<ErroresObra>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [creada, setCreada] = useState(false)
  const [enviando, setEnviando] = useState(false)

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
    siguienteId.current = 2
    setEtapas([{ id: 1, nombre: '' }])
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setCreada(false)
    setApiError(null)

    const nombresEtapas = etapas.map((et) => et.nombre)
    const erroresForm = validarObra({ nombre, ubicacion, fechaInicio, capatazId, etapas: nombresEtapas })
    if (hayErrores(erroresForm)) {
      setErrores(erroresForm)
      return
    }
    setErrores({})
    setEnviando(true)

    try {
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
      setEnviando(false)
    }
  }

  function claseInput(conError: boolean) {
    return conError ? `${styles.input} ${styles.inputInvalido}` : styles.input
  }

  return (
    <section className={styles.contenedor}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a Gestión y Compras
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
            onChange={(e) => setUbicacion(e.target.value)}
            aria-invalid={!!errores.ubicacion}
            aria-describedby={errores.ubicacion ? 'obra-ubicacion-error' : undefined}
          />
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
          {errores.fechaInicio && (
            <p id="obra-fecha-error" className={styles.errorCampo}>{errores.fechaInicio}</p>
          )}
        </div>

        {/* PROVISORIO: el backend todavía no tiene un endpoint para listar capataces,
            así que por ahora se pega el GUID. Cuando exista, esto pasa a ser un desplegable. */}
        <div className={styles.grupo}>
          <label htmlFor="obra-capataz" className={styles.label}>Capataz (identificador)</label>
          <input
            id="obra-capataz"
            className={claseInput(!!errores.capatazId)}
            value={capatazId}
            onChange={(e) => setCapatazId(e.target.value)}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={!!errores.capatazId}
            aria-describedby={errores.capatazId ? 'obra-capataz-error' : 'obra-capataz-ayuda'}
          />
          {errores.capatazId ? (
            <p id="obra-capataz-error" className={styles.errorCampo}>{errores.capatazId}</p>
          ) : (
            <p id="obra-capataz-ayuda" className={styles.ayuda}>
              Pegá el GUID del usuario con rol Capataz.
            </p>
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
