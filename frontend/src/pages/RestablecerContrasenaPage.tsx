import { type FormEvent, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { IconoOjo } from '../auth/IconoOjo'
import { restablecerPassword } from '../auth/passwordService'
import loginStyles from './LoginPage.module.css'
import styles from './RecuperarContrasenaPage.module.css'

// Mínimo de la regla actual de Identity (igual que el registro).
const LARGO_MINIMO = 6

interface ErroresCampo {
  password?: string
  repetir?: string
}

interface ErrorApi {
  mensaje: string
  /** true = el enlace o la contraseña fallaron: se ofrece pedir un enlace nuevo. */
  ofrecerEnlaceNuevo: boolean
}

function validarCampos(password: string, repetir: string): ErroresCampo {
  const errores: ErroresCampo = {}

  if (!password) {
    errores.password = 'La contraseña es obligatoria.'
  } else if (password.length < LARGO_MINIMO) {
    errores.password = `La contraseña debe tener al menos ${LARGO_MINIMO} caracteres.`
  }

  if (!repetir) {
    errores.repetir = 'Repetí la contraseña.'
  } else if (password && repetir !== password) {
    errores.repetir = 'Las contraseñas no coinciden.'
  }

  return errores
}

/** Pantalla a la que se llega desde el enlace de recuperación: /restablecer-contrasena?email=…&token=… */
export function RestablecerContrasenaPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()

  // URLSearchParams ya decodifica el enlace (por eso Usuarios lo arma con Uri.EscapeDataString:
  // el token puede traer + / =).
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''
  const enlaceCompleto = email !== '' && token !== ''

  const [password, setPassword] = useState('')
  const [repetir, setRepetir] = useState('')
  const [verPassword, setVerPassword] = useState(false)
  const [campoError, setCampoError] = useState<ErroresCampo>({})
  const [errorApi, setErrorApi] = useState<ErrorApi | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato contra el doble envío: el token es de un solo uso, así que el segundo
  // pedido fallaría con 400 y taparía el éxito del primero.
  const enviandoRef = useRef(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current) return

    // 1. Validación local: no llama a la API si hay errores de campo
    const errores = validarCampos(password, repetir)
    if (Object.keys(errores).length > 0) {
      setCampoError(errores)
      return
    }
    setCampoError({})
    setErrorApi(null)
    enviandoRef.current = true
    setEnviando(true)

    try {
      await restablecerPassword({ email, token, nuevaPassword: password })
      // replace: el enlace con el token no queda en el historial del navegador.
      navigate('/login', { replace: true, state: { contrasenaRestablecida: true } })
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        // El 400 junta "token inválido o vencido" y "contraseña que no cumple": no se
        // distinguen; se muestra el detalle del servidor y siempre se ofrece un enlace nuevo.
        setErrorApi({
          mensaje: err.detail ?? 'El enlace venció o la contraseña no cumple los requisitos.',
          ofrecerEnlaceNuevo: true,
        })
      } else {
        // Red caída, 500, etc.
        setErrorApi({
          mensaje: 'No se pudo conectar. Intentá de nuevo más tarde.',
          ofrecerEnlaceNuevo: false,
        })
      }
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  const marca = (
    <div className={loginStyles.marca}>
      <img src="/pwa-512x512.png" alt="" className={loginStyles.logo} />
      <span className={loginStyles.marcaTexto}>
        OBRA<span>SYNC</span>
      </span>
    </div>
  )

  // Se entró sin el enlace completo (falta el email o el token).
  if (!enlaceCompleto) {
    return (
      <div className={loginStyles.contenedor}>
        {marca}
        <section className={loginStyles.formulario}>
          <h1 className={loginStyles.titulo}>Restablecer contraseña</h1>
          <p className={loginStyles.errorApi} role="alert">
            El enlace no es válido o está incompleto.
          </p>
          <Link to="/recuperar-contrasena" className={styles.enlace}>
            Pedir un nuevo enlace
          </Link>
        </section>
      </div>
    )
  }

  const tipoCampo = verPassword ? 'text' : 'password'
  const idErrorApi = 'restablecer-error-api'

  return (
    <div className={loginStyles.contenedor}>
      {marca}

      <form
        className={loginStyles.formulario}
        onSubmit={handleSubmit}
        noValidate
        aria-describedby={errorApi ? idErrorApi : undefined}
      >
        <h1 className={loginStyles.titulo}>Elegí tu nueva contraseña</h1>
        <p className={styles.intro}>Para la cuenta {email}</p>

        {errorApi && (
          <div id={idErrorApi} className={loginStyles.errorApi} role="alert">
            <span>{errorApi.mensaje}</span>
          </div>
        )}
        {errorApi?.ofrecerEnlaceNuevo && (
          <Link to="/recuperar-contrasena" className={styles.enlace}>
            Pedir un nuevo enlace
          </Link>
        )}

        <div className={loginStyles.grupo}>
          <label htmlFor="restablecer-password" className={loginStyles.label}>
            Nueva contraseña
          </label>
          <div className={loginStyles.campoClave}>
            <input
              id="restablecer-password"
              type={tipoCampo}
              autoComplete="new-password"
              className={`${loginStyles.input} ${loginStyles.inputClave}${campoError.password ? ` ${loginStyles.inputInvalido}` : ''}`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-describedby={campoError.password ? 'restablecer-password-error' : undefined}
              aria-invalid={!!campoError.password}
            />
            {/* type="button": si no, dentro del <form> actuaría como "enviar".
                Un solo botón muestra u oculta las dos contraseñas. */}
            <button
              type="button"
              className={loginStyles.verClave}
              onClick={() => setVerPassword((visible) => !visible)}
              aria-label="Mostrar contraseña"
              aria-pressed={verPassword}
            >
              <IconoOjo tachado={verPassword} />
            </button>
          </div>
          {campoError.password && (
            <p id="restablecer-password-error" className={loginStyles.errorCampo} role="alert">
              {campoError.password}
            </p>
          )}
        </div>

        <div className={loginStyles.grupo}>
          <label htmlFor="restablecer-repetir" className={loginStyles.label}>
            Repetí la contraseña
          </label>
          <input
            id="restablecer-repetir"
            type={tipoCampo}
            autoComplete="new-password"
            className={`${loginStyles.input}${campoError.repetir ? ` ${loginStyles.inputInvalido}` : ''}`}
            value={repetir}
            onChange={(e) => setRepetir(e.target.value)}
            aria-describedby={campoError.repetir ? 'restablecer-repetir-error' : undefined}
            aria-invalid={!!campoError.repetir}
          />
          {campoError.repetir && (
            <p id="restablecer-repetir-error" className={loginStyles.errorCampo} role="alert">
              {campoError.repetir}
            </p>
          )}
        </div>

        <button
          type="submit"
          className={loginStyles.boton}
          disabled={enviando}
          aria-busy={enviando}
        >
          {enviando ? 'Guardando…' : 'Guardar contraseña'}
        </button>
      </form>
    </div>
  )
}
