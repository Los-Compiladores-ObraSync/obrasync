import { type KeyboardEvent, useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
import styles from '../pages/NuevaObraPage.module.css'
import { crearCapataz, type UsuarioCreado } from './capatacesService'
import { hayErroresCapataz, validarCapataz, type ErroresCapataz } from './validacionCapataz'

interface Props {
  /** Se llama con el capataz recién creado (para refrescar la lista y dejarlo seleccionado). */
  alCrear: (capataz: UsuarioCreado) => void
  alCancelar: () => void
}

function mensajeDeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 409) return err.detail ?? 'Ya existe un usuario con ese email.'
    if (err.status === 400) return err.detail ?? 'Hay datos inválidos. Revisá el formulario.'
    if (err.status === 403) return 'No tenés permiso para crear capataces.'
  }
  return 'No se pudo conectar. Intentá de nuevo más tarde.'
}

/**
 * Mini formulario para dar de alta un capataz sin salir de Nueva obra. Va DENTRO del formulario
 * de la obra, y un <form> no puede anidarse en otro: por eso usa botones type="button" y captura
 * el Enter a mano (si no, Enter enviaría la obra entera).
 */
export function CrearCapatazForm({ alCrear, alCancelar }: Props) {
  const [nombreCompleto, setNombreCompleto] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errores, setErrores] = useState<ErroresCapataz>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Mismo criterio que #109: un ref frena el doble clic antes de que el estado se actualice.
  const enviandoRef = useRef(false)

  async function crear() {
    if (enviandoRef.current) return
    setApiError(null)
    const erroresForm = validarCapataz({ nombreCompleto, email, password })
    if (hayErroresCapataz(erroresForm)) {
      setErrores(erroresForm)
      return
    }
    setErrores({})
    enviandoRef.current = true
    setEnviando(true)
    try {
      const creado = await crearCapataz({
        nombreCompleto: nombreCompleto.trim(),
        email: email.trim(),
        password,
      })
      alCrear(creado)
    } catch (err) {
      setApiError(mensajeDeError(err))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  function alTeclear(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'Enter' && e.target instanceof HTMLInputElement) {
      e.preventDefault()
      void crear()
    }
  }

  function claseInput(conError: boolean) {
    return conError ? `${styles.input} ${styles.inputInvalido}` : styles.input
  }

  return (
    <div role="group" aria-label="Crear capataz" className={styles.formulario} onKeyDown={alTeclear}>
      {apiError && (
        <p className={styles.errorApi} role="alert">
          {apiError}
        </p>
      )}

      <div className={styles.grupo}>
        <label htmlFor="capataz-nombre" className={styles.label}>Nombre completo del capataz</label>
        <input
          id="capataz-nombre"
          className={claseInput(!!errores.nombreCompleto)}
          value={nombreCompleto}
          onChange={(e) => setNombreCompleto(e.target.value)}
          aria-invalid={!!errores.nombreCompleto}
        />
        {errores.nombreCompleto && <p className={styles.errorCampo}>{errores.nombreCompleto}</p>}
      </div>

      <div className={styles.grupo}>
        <label htmlFor="capataz-email" className={styles.label}>Email del capataz</label>
        <input
          id="capataz-email"
          type="email"
          className={claseInput(!!errores.email)}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          aria-invalid={!!errores.email}
        />
        {errores.email && <p className={styles.errorCampo}>{errores.email}</p>}
      </div>

      <div className={styles.grupo}>
        <label htmlFor="capataz-password" className={styles.label}>Contraseña inicial</label>
        <input
          id="capataz-password"
          type="password"
          autoComplete="new-password"
          className={claseInput(!!errores.password)}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={!!errores.password}
        />
        <p className={styles.ayuda}>Se la pasás al capataz por tu cuenta. Mínimo 6 caracteres.</p>
        {errores.password && <p className={styles.errorCampo}>{errores.password}</p>}
      </div>

      <button type="button" className={styles.boton} disabled={enviando} onClick={() => void crear()}>
        {enviando ? 'Creando…' : 'Guardar capataz'}
      </button>
      <button type="button" className={styles.secundario} onClick={alCancelar}>
        Cancelar
      </button>
    </div>
  )
}
