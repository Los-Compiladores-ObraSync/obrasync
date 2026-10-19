import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { portalesParaRol } from '../portales/portales'
import styles from './LayoutApp.module.css'

function claseLink({ isActive }: { isActive: boolean }): string {
  return isActive ? `${styles.link} ${styles.activo}` : styles.link
}

/** Estructura común de las pantallas con sesión: encabezado, menú según el rol
 *  y, debajo, la pantalla que corresponda a la ruta (<Outlet />). */
export function LayoutApp() {
  const { usuario, logout } = useAuth()
  const portales = usuario ? portalesParaRol(usuario.rol) : []

  return (
    <>
      <header className="app-encabezado">
        <div className={styles.barra}>
          <span className={`app-marca ${styles.marca}`}>
            <img src="/pwa-192x192.png" alt="" className={styles.logo} />
            OBRA<span>SYNC</span>
          </span>
          <button type="button" className={styles.salir} onClick={logout}>
            Cerrar sesión
          </button>
        </div>

        <nav aria-label="Menú principal" className={styles.menu}>
          <ul className={styles.lista}>
            <li>
              <NavLink to="/" end className={claseLink}>
                Inicio
              </NavLink>
            </li>
            {portales.map((portal) => (
              <li key={portal.id}>
                <NavLink to={portal.ruta} className={claseLink}>
                  {portal.nombre}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </header>

      <main className="app-contenido">
        <Outlet />
      </main>
    </>
  )
}
