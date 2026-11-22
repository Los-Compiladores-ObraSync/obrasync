import { Link, Outlet } from 'react-router'
import { AvisosProvider } from '../notificaciones/AvisosProvider'
import { BloqueadasProvider } from '../notificaciones/BloqueadasProvider'
import { CampanaConPendientes } from '../notificaciones/CampanaConPendientes'
import { NovedadesProvider } from '../notificaciones/NovedadesProvider'
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
      <NovedadesProvider>
        <BloqueadasProvider>
          <AvisosProvider>
            <header className="app-encabezado">
              <div className={styles.barra}>
                <Link to="/" className={`app-marca ${styles.marca}`} title="Ir al inicio">
                  <img src="/pwa-512x512.png" alt="" className={styles.logo} />
                  OBRA<span>SYNC</span>
                </Link>
                <div className={styles.acciones}>
                  <CampanaConPendientes />
                  <MenuUsuario />
                </div>
              </div>
            </header>

            <main className="app-contenido">
              <Outlet />
            </main>
          </AvisosProvider>
        </BloqueadasProvider>
      </NovedadesProvider>
    </PendientesProvider>
  )
}
