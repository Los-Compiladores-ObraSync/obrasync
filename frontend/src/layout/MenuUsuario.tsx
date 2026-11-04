import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { iniciales } from '../auth/iniciales'
import { useAuth } from '../auth/useAuth'
import styles from './MenuUsuario.module.css'

/** Menú de la cuenta en el encabezado: círculo con las iniciales que abre
 *  "Cambiar contraseña" y "Cerrar sesión" (el lugar donde una app suele tener esas opciones). */
export function MenuUsuario() {
  const { usuario, logout } = useAuth()
  const [abierto, setAbierto] = useState(false)
  const contenedor = useRef<HTMLDivElement>(null)

  // Mientras el menú está abierto: se cierra al tocar afuera o con Escape.
  useEffect(() => {
    if (!abierto) return

    function alTocarAfuera(e: PointerEvent) {
      if (!contenedor.current?.contains(e.target as Node)) {
        setAbierto(false)
      }
    }
    function alPulsarTecla(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setAbierto(false)
      }
    }

    document.addEventListener('pointerdown', alTocarAfuera)
    document.addEventListener('keydown', alPulsarTecla)
    return () => {
      document.removeEventListener('pointerdown', alTocarAfuera)
      document.removeEventListener('keydown', alPulsarTecla)
    }
  }, [abierto])

  return (
    <div className={styles.contenedor} ref={contenedor}>
      <button
        type="button"
        className={styles.avatar}
        onClick={() => setAbierto((visible) => !visible)}
        aria-label="Menú de la cuenta"
        aria-expanded={abierto}
        aria-controls="menu-usuario"
      >
        {iniciales(usuario?.nombreCompleto ?? '')}
      </button>

      {abierto && (
        <div id="menu-usuario" className={styles.panel}>
          <p className={styles.nombre}>{usuario?.nombreCompleto}</p>
          <p className={styles.rol}>{usuario?.rol}</p>
          <p className={styles.email}>{usuario?.email}</p>
          <Link
            to="/cambiar-contrasena"
            className={styles.opcion}
            onClick={() => setAbierto(false)}
          >
            Cambiar contraseña
          </Link>
          <button type="button" className={styles.opcion} onClick={logout}>
            Cerrar sesión
          </button>
        </div>
      )}
    </div>
  )
}
