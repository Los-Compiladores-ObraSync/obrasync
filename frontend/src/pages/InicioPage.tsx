import { Link } from 'react-router'
import { iniciales } from '../auth/iniciales'
import { useAuth } from '../auth/useAuth'
import { InsigniaCantidad } from '../licitaciones/InsigniaCantidad'
import { ResumenLicitaciones } from '../licitaciones/ResumenLicitaciones'
import { useLicitacionesPorCotizar } from '../licitaciones/useLicitacionesPorCotizar'
import { ROLES_QUE_VEN_PENDIENTES } from '../notificaciones/configuracionAvisos'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'
import { textoLicitacionesParaCotizar, textoPendientes } from '../notificaciones/textos'
import { usePendientes } from '../notificaciones/usePendientes'
import { accionesParaRol } from '../portales/accionesRapidas'
import { portalesParaRol } from '../portales/portales'
import { ComprasPage } from './ComprasPage'
import styles from './InicioPage.module.css'

/** Desde el Historial se abre el detalle de cada requisición, donde se publica su licitación. */
const RUTA_HISTORIAL_REQUISICIONES = '/compras/requisiciones/historial'
/** Para el Proveedor, las licitaciones por cotizar figuran como "Pendientes" en su Historial de cotizaciones. */
const RUTA_HISTORIAL_COTIZACIONES = '/proveedores/mis-cotizaciones'

export function InicioPage() {
  const { usuario } = useAuth()
  const portales = usuario ? portalesParaRol(usuario.rol) : []

  const todasLasAcciones = usuario ? accionesParaRol(usuario.rol) : []
  // Las acciones destacadas (el asistente de IA) van en una tarjeta grande arriba de todo.
  const destacadas = todasLasAcciones.filter((accion) => accion.destacada)
  const acciones = todasLasAcciones.filter((accion) => !accion.destacada)
  // Quien aprueba requisiciones (Comprador, Admin) ve primero cuántas esperan; el resto de las
  // opciones pasan a ser botones secundarios.
  const apruebaRequisiciones = usuario ? ROLES_QUE_VEN_PENDIENTES.includes(usuario.rol) : false
  // El Proveedor cotiza: su Inicio muestra cuántas licitaciones le faltan cotizar (como el Comprador con sus requisiciones).
  const cotiza = usuario?.rol === 'Proveedor'
  // El mismo número que muestra la campana: el Historial es donde se sigue cada requisición.
  const { cantidad: pendientes } = usePendientes()
  const resaltarHistorial = apruebaRequisiciones && pendientes > 0
  // Para el Proveedor, la tarjeta de arriba cuenta las licitaciones que le faltan cotizar.
  const licitaciones = useLicitacionesPorCotizar(cotiza)
  const porCotizar = licitaciones.cantidad ?? 0
  // Igual que el Historial de requisiciones del Comprador: se resalta mientras haya pendientes.
  const resaltarCotizaciones = cotiza && porCotizar > 0

  return (
    <>
      <h1>Bienvenido, {usuario?.nombreCompleto}</h1>

      {portales.length === 0 && (
        <p role="status" className={styles.aviso}>
          Tu rol todavía no tiene portales asignados. Consultá con un administrador.
        </p>
      )}
      {destacadas.map((accion) => (
        <Link
          key={accion.ruta}
          to={accion.ruta}
          className={styles.destacada}
          aria-label={accion.texto}
          aria-describedby={accion.descripcion ? 'descripcion-accion-destacada' : undefined}
        >
          <span className={styles.destacadaIcono} aria-hidden="true">
            <svg viewBox="0 0 24 24" width="32" height="32" fill="currentColor" focusable="false">
              <path d="M9 3l1.8 4.7L15.5 9.5l-4.7 1.8L9 16l-1.8-4.7L2.5 9.5l4.7-1.8L9 3z" />
              <path d="M18 12l1.1 2.9 2.9 1.1-2.9 1.1L18 20l-1.1-2.9-2.9-1.1 2.9-1.1L18 12z" />
            </svg>
          </span>
          <span className={styles.destacadaTextos}>
            <span className={styles.destacadaTitulo}>
              {accion.texto}
              <span className={styles.destacadaEtiqueta} aria-hidden="true">
                IA
              </span>
            </span>
            {accion.descripcion && (
              <span id="descripcion-accion-destacada" className={styles.destacadaDescripcion}>
                {accion.descripcion}
              </span>
            )}
          </span>
          <span className={styles.destacadaFlecha} aria-hidden="true">
            ›
          </span>
        </Link>
      ))}
      {apruebaRequisiciones && <ResumenAprobacion />}
      {cotiza && <ResumenLicitaciones {...licitaciones} />}
      {usuario?.rol === 'Comprador' && <ComprasPage integrada descripcionPendientes={textoPendientes(pendientes)} />}
      {acciones.length > 0 && usuario?.rol !== 'Comprador' && (
        <ul className="lista-acciones">
          {acciones.map((accion) => (
            <li key={accion.ruta}>
              <Link
                to={accion.ruta}
                className={
                  apruebaRequisiciones || cotiza
                    ? (accion.ruta === RUTA_HISTORIAL_REQUISICIONES && resaltarHistorial) ||
                      (accion.ruta === RUTA_HISTORIAL_COTIZACIONES && resaltarCotizaciones)
                      ? `accion-secundaria ${styles.conPendientes}`
                      : 'accion-secundaria'
                    : 'accion-grande'
                }
              >
                {accion.ruta === RUTA_HISTORIAL_REQUISICIONES && resaltarHistorial ? (
                  <span>
                    {accion.texto}
                    <InsigniaCantidad cantidad={pendientes} descripcion={textoPendientes(pendientes)} />
                  </span>
                ) : accion.ruta === RUTA_HISTORIAL_COTIZACIONES && resaltarCotizaciones ? (
                  <span>
                    {accion.texto}
                    <InsigniaCantidad
                      cantidad={porCotizar}
                      descripcion={textoLicitacionesParaCotizar(porCotizar)}
                    />
                  </span>
                ) : (
                  accion.texto
                )}
              </Link>
            </li>
          ))}
        </ul>
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
