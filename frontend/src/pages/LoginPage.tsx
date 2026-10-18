import { type FormEvent, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { loginApi } from '../auth/authService'
import { useAuth } from '../auth/useAuth'
import styles from './LoginPage.module.css'

// ── Validación ────────────────────────────────────────────────────────────────

const RE_EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface ErroresCampo {
  email?: string
  password?: string
}

function validarCampos(email: string, password: string): ErroresCampo {
  const errores: ErroresCampo = {}

  if (!email.trim()) {
    errores.email = 'El email es obligatorio.'
  } else if (!RE_EMAIL.test(email)) {
    errores.email = 'Ingresá un email válido.'
  }

  if (!password) {
    errores.password = 'La contraseña es obligatoria.'
  }

  return errores
}

// ── Icono del botón "ver contraseña" ─────────────────────────────────────────
// SVG en línea (sin librerías). aria-hidden: el nombre del botón lo da su aria-label.

function IconoOjo({ tachado }: { tachado: boolean }) {
  return (
    <svg
      width="22"
      height="22"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z" />
      <circle cx="12" cy="12" r="3" />
      {tachado && <line x1="3" y1="3" x2="21" y2="21" />}
    </svg>
  )
}

// ── Componente ────────────────────────────────────────────────────────────────

export function LoginPage() {
  const auth = useAuth()

  // Campos del formulario
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  // true = la contraseña se ve en texto; false = puntitos (valor inicial)
  const [verPassword, setVerPassword] = useState(false)

  // Errores de validación por campo (antes de enviar)
  const [campoError, setCampoError] = useState<ErroresCampo>({})

  // Mensaje de error del servidor (después de enviar)
  const [apiError, setApiError] = useState<string | null>(null)

  // Bloquea el botón mientras hay un fetch en curso
  const [enviando, setEnviando] = useState(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()

    // 1. Validación local — no llama a la API si hay errores de campo
    const errores = validarCampos(email, password)
    if (Object.keys(errores).length > 0) {
      setCampoError(errores)
      return
    }
    setCampoError({})
    setApiError(null)
    setEnviando(true)

    try {
      // 2. Llamada al servicio (MSW la intercepta en desarrollo con mocks)
      const { usuario, token } = await loginApi({ email, password })

      // 3. Guarda sesión en localStorage y actualiza el contexto
      auth.login(usuario, token)
    } catch (err) {
      // 4a. Credenciales inválidas → 401
      if (err instanceof ApiError && err.status === 401) {
        setApiError('Credenciales inválidas. Revisá tu email y contraseña.')
      } else {
        // 4b. Cualquier otro error (red caída, 500, etc.)
        setApiError('No se pudo conectar. Intentá de nuevo más tarde.')
      }
    } finally {
      setEnviando(false)
    }
  }

  // ID para asociar el error de API con el botón de submit (accesibilidad)
  const idErrorApi = 'login-error-api'

  return (
    <div className={styles.contenedor}>
      {/* Logotipo de la marca */}
      <div className={styles.marca}>
        <img src="/pwa-192x192.png" alt="" className={styles.logo} />
        <span className={styles.marcaTexto}>
          OBRA <span>SYNC</span>
        </span>
      </div>

      <form
        className={styles.formulario}
        onSubmit={handleSubmit}
        noValidate
        aria-describedby={apiError ? idErrorApi : undefined}
      >
        <h1 className={styles.titulo}>Iniciar sesión</h1>

        {/* Mensaje de error del servidor — role="alert" para lectores de pantalla */}
        {apiError && (
          <p id={idErrorApi} className={styles.errorApi} role="alert">
            {apiError}
          </p>
        )}

        {/* Campo email */}
        <div className={styles.grupo}>
          <label htmlFor="login-email" className={styles.label}>
            Email
          </label>
          <input
            id="login-email"
            type="email"
            autoComplete="email"
            className={`${styles.input}${campoError.email ? ` ${styles.inputInvalido}` : ''}`}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-describedby={campoError.email ? 'login-email-error' : undefined}
            aria-invalid={!!campoError.email}
          />
          {campoError.email && (
            <p id="login-email-error" className={styles.errorCampo} role="alert">
              {campoError.email}
            </p>
          )}
        </div>

        {/* Campo contraseña */}
        <div className={styles.grupo}>
          <label htmlFor="login-password" className={styles.label}>
            Contraseña
          </label>
          <div className={styles.campoClave}>
            <input
              id="login-password"
              type={verPassword ? 'text' : 'password'}
              autoComplete="current-password"
              className={`${styles.input} ${styles.inputClave}${campoError.password ? ` ${styles.inputInvalido}` : ''}`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-describedby={campoError.password ? 'login-password-error' : undefined}
              aria-invalid={!!campoError.password}
            />
            {/* type="button": si no, dentro del <form> actuaría como "enviar".
                El nombre es fijo y aria-pressed indica el estado (patrón de botón de dos estados). */}
            <button
              type="button"
              className={styles.verClave}
              onClick={() => setVerPassword((visible) => !visible)}
              aria-label="Mostrar contraseña"
              aria-pressed={verPassword}
            >
              <IconoOjo tachado={verPassword} />
            </button>
          </div>
          {campoError.password && (
            <p id="login-password-error" className={styles.errorCampo} role="alert">
              {campoError.password}
            </p>
          )}
        </div>

        {/* Botón de submit */}
        <button
          type="submit"
          className={styles.boton}
          disabled={enviando}
          aria-busy={enviando}
        >
          {enviando ? 'Ingresando…' : 'Ingresar'}
        </button>
      </form>
    </div>
  )
}
