import { Outlet } from 'react-router'
import { CampanaConPendientes } from '../notificaciones/CampanaConPendientes'
import { PendientesProvider } from '../notificaciones/PendientesProvider'
import styles from './LayoutApp.module.css'
import { MenuUsuario } from './MenuUsuario'

/** Estructura común de las pantallas con sesión: encabezado (marca, avisos y menú de la cuenta)
 *  y, debajo, la pantalla que corresponda a la ruta (<Outlet />).
 *  No hay barra de pestañas: la pantalla de Inicio de cada rol ya trae todo lo que ese rol hace,
 *  y las demás pantallas tienen un enlace "← Volver al inicio". */
export function LayoutApp() {
  return (
    <PendientesProvider>
      <header className="app-encabezado">
        <div className={styles.barra}>
          <span className={`app-marca ${styles.marca}`}>
            <img src="/pwa-512x512.png" alt="" className={styles.logo} />
            OBRA<span>SYNC</span>
          </span>
          <div className={styles.acciones}>
            <CampanaConPendientes />
            <MenuUsuario />
          </div>
        </div>
      </header>

      <main className="app-contenido">
        <Outlet />
      </main>
    </PendientesProvider>
  )
}
