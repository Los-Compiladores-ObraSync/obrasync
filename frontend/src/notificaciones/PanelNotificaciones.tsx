import { Link } from 'react-router'
import styles from './PanelNotificaciones.module.css'

interface Props {
    /** Id que usa la campana en aria-controls. */
    id: string
    /** Requisiciones pendientes de aprobar. */
    cantidad: number
    /** Se llama al tocar el enlace, para que el panel se cierre. */
    onNavegar: () => void
}

/** Texto del aviso, en singular cuando hay una sola. */
function textoAviso(cantidad: number): string {
    return cantidad === 1
        ? 'Tenés 1 requisición para aprobar'
        : `Tenés ${cantidad} requisiciones para aprobar`
}

/** Panel que se despliega debajo de la campana (#49). */
export function PanelNotificaciones({ id, cantidad, onNavegar }: Props) {
    return (
        <div id= { id } className = { styles.panel } role = "region" aria-label="Avisos" >
        { cantidad > 0 ? (
            <>
            <p className= { styles.texto } > { textoAviso(cantidad) } </p>
        < Link to = "/compras/requisiciones" className = { styles.enlace } onClick = { onNavegar } >
            Ir a Aprobar requisiciones
                </Link>
                </>
      ) : (
        <p className= { styles.vacio } > No tenés avisos </p>
      )
}
</div>
  )
}