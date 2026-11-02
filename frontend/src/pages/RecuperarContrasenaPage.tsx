import { type FormEvent, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { solicitarRecuperacionPassword } from '../auth/passwordService'
import { validarEmail } from '../auth/validacionEmail'
import loginStyles from './LoginPage.module.css'
import styles from './RecuperarContrasenaPage.module.css'

// Mensaje igual exista o no la cuenta: no se revela qué emails están registrados.
const MENSAJE_NEUTRO =
  'Si el email corresponde a una cuenta, te enviamos las instrucciones para restablecer tu contraseña.'

export function RecuperarContrasenaPage() {
  const [email, setEmail] = useState('')
  const [errorEmail, setErrorEmail] = useState<string | undefined>()
  const [apiError, setApiError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [enviado, setEnviado] = useState(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()

    // 1. Validación local: no llama a la API si el email está mal
    const error = validarEmail(email)
    if (error) {
      setErrorEmail(error)
      return
    }
    setErrorEmail(undefined)
    setApiError(null)
    setEnviando(true)

    try {
      await solicitarRecuperacionPassword(email.trim())
      setEnviado(true)
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        setApiError(err.detail ?? 'Revisá el email e intentá de nuevo.')
      } else {
        // Red caída, 500, etc.
        setApiError('No se pudo conectar. Intentá de nuevo más tarde.')
      }
    } finally {
      setEnviando(false)
    }
  }

  const idErrorApi = 'recuperar-error-api'

  return (
    <div className={loginStyles.contenedor}>
      <div className={loginStyles.marca}>
        <img src="/pwa-512x512.png" alt="" className={loginStyles.logo} />
        <span className={loginStyles.marcaTexto}>
          OBRA<span>SYNC</span>
        </span>
      </div>

      {enviado ? (
        <section className={loginStyles.formulario}>
          <h1 className={loginStyles.titulo}>Revisá tu correo</h1>
          <p className={styles.exito} role="status">
            {MENSAJE_NEUTRO}
          </p>
          <Link to="/login" className={styles.enlace}>
            Volver a iniciar sesión
          </Link>
        </section>
      ) : (
        <form
          className={loginStyles.formulario}
          onSubmit={handleSubmit}
          noValidate
          aria-describedby={apiError ? idErrorApi : undefined}
        >
          <h1 className={loginStyles.titulo}>Recuperar contraseña</h1>
          <p className={styles.intro}>
            Ingresá tu email y te enviamos las instrucciones para elegir una nueva contraseña.
          </p>

          {apiError && (
            <p id={idErrorApi} className={loginStyles.errorApi} role="alert">
              {apiError}
            </p>
          )}

          <div className={loginStyles.grupo}>
            <label htmlFor="recuperar-email" className={loginStyles.label}>
              Email
            </label>
            <input
              id="recuperar-email"
              type="email"
              autoComplete="email"
              className={`${loginStyles.input}${errorEmail ? ` ${loginStyles.inputInvalido}` : ''}`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-describedby={errorEmail ? 'recuperar-email-error' : undefined}
              aria-invalid={!!errorEmail}
            />
            {errorEmail && (
              <p id="recuperar-email-error" className={loginStyles.errorCampo} role="alert">
                {errorEmail}
              </p>
            )}
          </div>

          <button
            type="submit"
            className={loginStyles.boton}
            disabled={enviando}
            aria-busy={enviando}
          >
            {enviando ? 'Enviando…' : 'Enviar instrucciones'}
          </button>

          <Link to="/login" className={styles.enlace}>
            Volver a iniciar sesión
          </Link>
        </form>
      )}
    </div>
  )
}
