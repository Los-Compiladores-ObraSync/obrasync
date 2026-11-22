import { Link } from 'react-router'
import { agruparPorOrden, type Aviso } from './avisosFlujo'
import { MAX_AVISOS_EN_CAMPANA } from './configuracionAvisos'
import styles from './PanelNotificaciones.module.css'

interface Props {
  avisos: Aviso[]
  /** Se llama al tocar un aviso, para que el panel se cierre. */
  onNavegar: () => void
  /** Se llama al abrir un aviso (las novedades se marcan como vistas). */
  onAbrir?: (aviso: Aviso) => void
}

/** Lista de avisos del flujo de órdenes (#219). Los avisos de la misma orden se muestran juntos bajo su
 *  nombre; cada uno lleva directo a su pantalla. Muestra pocos renglones para que entre en el celular. */
export function ListaAvisos({ avisos, onNavegar, onAbrir }: Props) {
  const renglones = agruparPorOrden(avisos)
  const visibles = renglones.slice(0, MAX_AVISOS_EN_CAMPANA)
  const ocultos = renglones.length - visibles.length

  function fila(aviso: Aviso, texto: string) {
    return (
      <Link
        to={aviso.ruta}
        className={styles.fila}
        onClick={() => {
          onAbrir?.(aviso)
          onNavegar()
        }}
      >
        <span className={styles.numero}>{texto}</span>
        {aviso.detalle && <span className={styles.detalle}>{aviso.detalle}</span>}
      </Link>
    )
  }

  return (
    <>
      <ul className={styles.lista}>
        {visibles.map((renglon) =>
          renglon.titulo ? (
            <li key={renglon.avisos[0].grupo?.id} className={styles.grupo}>
              <p className={styles.grupoTitulo}>{renglon.titulo}</p>
              <ul className={styles.lista}>
                {renglon.avisos.map((aviso) => (
                  <li key={aviso.id}>{fila(aviso, aviso.textoCorto)}</li>
                ))}
              </ul>
            </li>
          ) : (
            <li key={renglon.avisos[0].id}>{fila(renglon.avisos[0], renglon.avisos[0].texto)}</li>
          ),
        )}
      </ul>
      {ocultos > 0 && <p className={styles.vacio}>y {ocultos} más</p>}
    </>
  )
}
