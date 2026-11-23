import { type FormEvent, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { registrarProveedor } from '../auth/registroService'
import { IconoOjo } from '../auth/IconoOjo'
import { useAuth } from '../auth/useAuth'
import { validarRegistro, type ErroresRegistro } from '../auth/validacionRegistro'
import styles from './RegistroProveedorPage.module.css'

export function RegistroProveedorPage() {
  const auth = useAuth()
  const [nombreCompleto, setNombreCompleto] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [verPassword, setVerPassword] = useState(false)
  const [campoError, setCampoError] = useState<ErroresRegistro>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  async function handleSubmit(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault()
    const errores = validarRegistro(nombreCompleto, email, password)
    if (Object.keys(errores).length) {
      setCampoError(errores)
      return
    }
    setCampoError({})
    setApiError(null)
    setEnviando(true)
    try {
      const { usuario, token } = await registrarProveedor({ nombreCompleto, email, password })
      auth.login(usuario, token)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setApiError('Ya existe una cuenta con ese email. Iniciá sesión desde el enlace de abajo.')
      } else if (error instanceof ApiError && error.status === 400) {
        setApiError(error.detail || 'Revisá los datos ingresados.')
      } else if (error instanceof ApiError && error.status === 429) {
        setApiError('Demasiados intentos. Esperá un minuto y probá de nuevo.')
      } else {
        setApiError('No se pudo conectar. Intentá de nuevo más tarde.')
      }
    } finally {
      setEnviando(false)
    }
  }

  const idErrorApi = 'registro-error-api'

  return (
    <div className={styles.contenedor}>
      <div className={styles.marca}>
        <img src="/pwa-512x512.png" alt="" className={styles.logo} />
        <span className={styles.marcaTexto}>OBRA<span>SYNC</span></span>
      </div>
      <form className={styles.formulario} onSubmit={handleSubmit} noValidate aria-describedby={apiError ? idErrorApi : undefined}>
        <h1 className={styles.titulo}>Crear cuenta de proveedor</h1>
        {apiError && <p id={idErrorApi} className={styles.errorApi} role="alert">{apiError}</p>}

        <div className={styles.grupo}>
          <label htmlFor="registro-nombre" className={styles.label}>Nombre completo</label>
          <input id="registro-nombre" type="text" autoComplete="name" className={`${styles.input}${campoError.nombreCompleto ? ` ${styles.inputInvalido}` : ''}`} value={nombreCompleto} onChange={(e) => setNombreCompleto(e.target.value)} aria-describedby={campoError.nombreCompleto ? 'registro-nombre-error' : undefined} aria-invalid={!!campoError.nombreCompleto} />
          {campoError.nombreCompleto && <p id="registro-nombre-error" className={styles.errorCampo} role="alert">{campoError.nombreCompleto}</p>}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="registro-email" className={styles.label}>Email</label>
          <input id="registro-email" type="email" autoComplete="email" className={`${styles.input}${campoError.email ? ` ${styles.inputInvalido}` : ''}`} value={email} onChange={(e) => setEmail(e.target.value)} aria-describedby={campoError.email ? 'registro-email-error' : undefined} aria-invalid={!!campoError.email} />
          {campoError.email && <p id="registro-email-error" className={styles.errorCampo} role="alert">{campoError.email}</p>}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="registro-password" className={styles.label}>Contraseña</label>
          <div className={styles.campoClave}>
            <input id="registro-password" type={verPassword ? 'text' : 'password'} autoComplete="new-password" className={`${styles.input} ${styles.inputClave}${campoError.password ? ` ${styles.inputInvalido}` : ''}`} value={password} onChange={(e) => setPassword(e.target.value)} aria-describedby={campoError.password ? 'registro-password-error' : undefined} aria-invalid={!!campoError.password} />
            <button type="button" className={styles.verClave} onClick={() => setVerPassword((visible) => !visible)} aria-label="Mostrar contraseña" aria-pressed={verPassword}><IconoOjo tachado={verPassword} /></button>
          </div>
          {campoError.password && <p id="registro-password-error" className={styles.errorCampo} role="alert">{campoError.password}</p>}
        </div>

        <button type="submit" className={styles.boton} disabled={enviando} aria-busy={enviando}>{enviando ? 'Creando cuenta…' : 'Crear cuenta'}</button>
        <p className={styles.textoLogin}>¿Ya tenés cuenta? <Link to="/login" className={styles.enlace}>Iniciá sesión</Link></p>
      </form>
    </div>
  )
}
