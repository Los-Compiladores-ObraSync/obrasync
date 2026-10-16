import { useAuth } from '../auth/useAuth'
import styles from './InicioPage.module.css'

export function InicioPage() {
  const { usuario, logout } = useAuth()

  return (
    <>
      <header className="app-encabezado">
        <span className={`app-marca ${styles.marca}`}>
          <img
            src="/pwa-192x192.png"
            alt=""
            className={styles.logo}
          />
          OBRA <span>SYNC</span>
        </span>
      </header>

      <main className="app-contenido">
        <h1>Bienvenido, {usuario?.nombreCompleto}</h1>
        <p>
          <strong>Rol:</strong> {usuario?.rol}
        </p>
        <p>
          <strong>Email:</strong> {usuario?.email}
        </p>
        <button type="button" onClick={logout}>
          Cerrar sesión
        </button>
      </main>
    </>
  )
}
