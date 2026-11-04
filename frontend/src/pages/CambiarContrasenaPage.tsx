import { type FormEvent, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { IconoOjo } from '../auth/IconoOjo'
import { cambiarPassword } from '../auth/passwordService'
import styles from './CambiarContrasenaPage.module.css'

// Mínimo de la regla actual de Identity (igual que el registro y restablecer).
const LARGO_MINIMO = 6

interface ErroresCampo {
  actual?: string
  nueva?: string
  repetir?: string
}

function validarCampos(actual: string, nueva: string, repetir: string): ErroresCampo {
  const errores: ErroresCampo = {}

  if (!actual) {
    errores.actual = 'Ingresá tu contraseña actual.'
  }

  if (!nueva) {
    errores.nueva = 'La nueva contraseña es obligatoria.'
  } else if (nueva.length < LARGO_MINIMO) {
    errores.nueva = `La contraseña debe tener al menos ${LARGO_MINIMO} caracteres.`
  } else if (actual && nueva === actual) {
    errores.nueva = 'La nueva contraseña tiene que ser distinta de la actual.'
  }

  if (!repetir) {
    errores.repetir = 'Repetí la nueva contraseña.'
  } else if (nueva && repetir !== nueva) {
    errores.repetir = 'Las contraseñas no coinciden.'
  }

  return errores
}

/** Pantalla para cambiar la contraseña con la sesión iniciada (cualquier rol): /cambiar-contrasena */
export function CambiarContrasenaPage() {
  const [actual, setActual] = useState('')
  const [nueva, setNueva] = useState('')
  const [repetir, setRepetir] = useState('')
  const [verPassword, setVerPassword] = useState(false)
  const [campoError, setCampoError] = useState<ErroresCampo>({})
  const [errorApi, setErrorApi] = useState<string | null>(null)
  const [exito, setExito] = useState(false)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato contra el doble envío (mismo criterio que #109): el estado `enviando`
  // se actualiza recién en el próximo render, y un segundo submit podría colarse antes.
  const enviandoRef = useRef(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current) return

    // 1. Validación local: no llama a la API si hay errores de campo
    const errores = validarCampos(actual, nueva, repetir)
    if (Object.keys(errores).length > 0) {
      setCampoError(errores)
      setExito(false)
      return
    }
    setCampoError({})
    setErrorApi(null)
    setExito(false)
    enviandoRef.current = true
    setEnviando(true)

    try {
      await cambiarPassword({
        passwordActual: actual,
        nuevaPassword: nueva,
        confirmarPassword: repetir,
      })
      // La sesión sigue abierta: se limpian los campos para no dejar las claves a la vista.
      setActual('')
      setNueva('')
      setRepetir('')
      setVerPassword(false)
      setExito(true)
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        // Sesión vencida: apiFetch ya la cerró y la app vuelve sola al login.
        // No se muestra nada acá porque la pantalla se va a desmontar.
      } else if (err instanceof ApiError && err.status === 400) {
        // Contraseña actual incorrecta, igual a la actual, no coincide o no cumple la política:
        // el servidor ya manda el motivo en español.
        setErrorApi(err.detail ?? 'No se pudo cambiar la contraseña. Revisá los datos.')
      } else {
        // Red caída, 500, etc.
        setErrorApi('No se pudo conectar. Intentá de nuevo más tarde.')
      }
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  const tipoCampo = verPassword ? 'text' : 'password'
  const idErrorApi = 'cambiar-error-api'

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Cambiar contraseña</h1>

      <form
        className={styles.formulario}
        onSubmit={handleSubmit}
        noValidate
        aria-describedby={errorApi ? idErrorApi : undefined}
      >
        {exito && (
          <p className={styles.exito} role="status">
            Tu contraseña se cambió correctamente.
          </p>
        )}
        {errorApi && (
          <p id={idErrorApi} className={styles.errorApi} role="alert">
            {errorApi}
          </p>
        )}

        <div className={styles.grupo}>
          <label htmlFor="cambiar-actual" className={styles.label}>
            Contraseña actual
          </label>
          <div className={styles.campoClave}>
            <input
              id="cambiar-actual"
              type={tipoCampo}
              autoComplete="current-password"
              className={`${styles.input} ${styles.inputClave}${campoError.actual ? ` ${styles.inputInvalido}` : ''}`}
              value={actual}
              onChange={(e) => setActual(e.target.value)}
              aria-describedby={campoError.actual ? 'cambiar-actual-error' : undefined}
              aria-invalid={!!campoError.actual}
            />
            {/* type="button": si no, dentro del <form> actuaría como "enviar".
                Un solo botón muestra u oculta las tres contraseñas. */}
            <button
              type="button"
              className={styles.verClave}
              onClick={() => setVerPassword((visible) => !visible)}
              aria-label="Mostrar contraseñas"
              aria-pressed={verPassword}
            >
              <IconoOjo tachado={verPassword} />
            </button>
          </div>
          {campoError.actual && (
            <p id="cambiar-actual-error" className={styles.errorCampo} role="alert">
              {campoError.actual}
            </p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="cambiar-nueva" className={styles.label}>
            Nueva contraseña
          </label>
          <input
            id="cambiar-nueva"
            type={tipoCampo}
            autoComplete="new-password"
            className={`${styles.input}${campoError.nueva ? ` ${styles.inputInvalido}` : ''}`}
            value={nueva}
            onChange={(e) => setNueva(e.target.value)}
            aria-describedby={campoError.nueva ? 'cambiar-nueva-error' : undefined}
            aria-invalid={!!campoError.nueva}
          />
          {campoError.nueva && (
            <p id="cambiar-nueva-error" className={styles.errorCampo} role="alert">
              {campoError.nueva}
            </p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="cambiar-repetir" className={styles.label}>
            Repetí la nueva contraseña
          </label>
          <input
            id="cambiar-repetir"
            type={tipoCampo}
            autoComplete="new-password"
            className={`${styles.input}${campoError.repetir ? ` ${styles.inputInvalido}` : ''}`}
            value={repetir}
            onChange={(e) => setRepetir(e.target.value)}
            aria-describedby={campoError.repetir ? 'cambiar-repetir-error' : undefined}
            aria-invalid={!!campoError.repetir}
          />
          {campoError.repetir && (
            <p id="cambiar-repetir-error" className={styles.errorCampo} role="alert">
              {campoError.repetir}
            </p>
          )}
        </div>

        <button type="submit" className={styles.boton} disabled={enviando} aria-busy={enviando}>
          {enviando ? 'Guardando…' : 'Cambiar contraseña'}
        </button>
      </form>
    </section>
  )
}
