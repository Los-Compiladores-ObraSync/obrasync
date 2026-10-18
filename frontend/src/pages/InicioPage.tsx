import { useAuth } from '../auth/useAuth'
import { portalesParaRol } from '../portales/portales'
import styles from './InicioPage.module.css'

export function InicioPage() {
  const { usuario } = useAuth()
  const portales = usuario ? portalesParaRol(usuario.rol) : []

  return (
    <>
      <h1>Bienvenido, {usuario?.nombreCompleto}</h1>
      <p>
        <strong>Rol:</strong> {usuario?.rol}
      </p>
      <p>
        <strong>Email:</strong> {usuario?.email}
      </p>
      {portales.length === 0 && (
        <p role="status" className={styles.aviso}>
          Tu rol todavía no tiene portales asignados. Consultá con un administrador.
        </p>
      )}
    </>
  )
}
