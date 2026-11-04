import { Link } from 'react-router'
import { iniciales } from '../auth/iniciales'
import { useAuth } from '../auth/useAuth'
import { ROLES_QUE_VEN_PENDIENTES } from '../notificaciones/configuracionAvisos'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'
import { accionesParaRol } from '../portales/accionesRapidas'
import { portalesParaRol } from '../portales/portales'
import styles from './InicioPage.module.css'

export function InicioPage() {
  const { usuario } = useAuth()
  const portales = usuario ? portalesParaRol(usuario.rol) : []

  const acciones = usuario ? accionesParaRol(usuario.rol) : []
  // Quien aprueba requisiciones (Comprador, Admin) ve primero cuántas esperan; el resto de las
  // opciones pasan a ser botones secundarios.
  const apruebaRequisiciones = usuario ? ROLES_QUE_VEN_PENDIENTES.includes(usuario.rol) : false
  // Si el rol no tiene accesos rápidos (hoy, el Proveedor), se muestran sus portales.
  const botones =
    acciones.length > 0
      ? acciones.map((a) => ({ clave: a.ruta, ruta: a.ruta, texto: a.texto }))
      : portales.map((p) => ({ clave: p.id, ruta: p.ruta, texto: `Ir a ${p.nombre}` }))

  return (
    <>
      <h1>Bienvenido, {usuario?.nombreCompleto}</h1>

      {portales.length === 0 && (
        <p role="status" className={styles.aviso}>
          Tu rol todavía no tiene portales asignados. Consultá con un administrador.
        </p>
      )}
      {apruebaRequisiciones && <ResumenAprobacion />}
      {botones.length > 0 && (
        <>
          {!apruebaRequisiciones && (
            <h2 className={styles.subtitulo}>
              {acciones.length > 0 ? 'Accesos rápidos' : '¿A dónde vas?'}
            </h2>
          )}
          <ul className="lista-acciones">
            {botones.map((boton) => (
              <li key={boton.clave}>
                <Link
                  to={boton.ruta}
                  className={apruebaRequisiciones ? 'accion-secundaria' : 'accion-grande'}
                >
                  {boton.texto}
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}

      <section className={styles.perfil} aria-label="Tus datos">
        <div className={styles.avatar} aria-hidden="true">
          {iniciales(usuario?.nombreCompleto ?? '')}
        </div>
        <div className={styles.datos}>
          <p className={styles.dato}>
            <span className={styles.etiqueta}>Rol</span>
            <span className={styles.rol}>{usuario?.rol}</span>
          </p>
          <p className={styles.dato}>
            <span className={styles.etiqueta}>Email</span>
            <span className={styles.valor}>{usuario?.email}</span>
          </p>
        </div>
      </section>
    </>
  )
}
