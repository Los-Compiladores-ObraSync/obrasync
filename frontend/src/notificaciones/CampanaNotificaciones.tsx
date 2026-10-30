import type { Ref } from 'react'
import styles from './CampanaNotificaciones.module.css'

interface Props {
    /** Cantidad de avisos sin leer. Con 0 (o menos) no se muestra el número. */
    cantidad?: number
    /** Si el panel de avisos está abierto (lo usa el lector de pantalla). */
    abierto?: boolean
    /** Id del panel que abre esta campana, para relacionarlos (aria-controls). */
    idPanel?: string
    /** Resalta la campana (color, animación y otro texto para el lector de pantalla).
     *  Se usa cuando hay muchas requisiciones pendientes (#50). */
    resaltada?: boolean
    /** Qué hacer al tocar la campana (abrir o cerrar el panel). */
    onClick?: () => void
    /** Referencia al botón, para poder devolverle el foco al cerrar el panel. */
    ref?: Ref<HTMLButtonElement>
}

/** Texto que escucha un lector de pantalla: el ícono solo no dice nada. */
function textoAccesible(cantidad: number, resaltada: boolean): string {
    if (cantidad <= 0) return 'Notificaciones: sin avisos'
    // No alcanza con el color: quien no lo ve también tiene que enterarse.
    if (resaltada) return `Atención: ${cantidad} requisiciones pendientes`
    return `Notificaciones: ${cantidad} sin leer`
}

/** Campana del encabezado: ícono y contador (badge). Solo muestra la cantidad
 *  que le pasan (#47, #48); el panel que se abre al tocarla es #49. */
export function CampanaNotificaciones({
    cantidad = 0,
    resaltada = false,
    abierto,
    idPanel,
    onClick,
    ref,
}: Props) {
    const hayAvisos = cantidad > 0
    const estaResaltada = hayAvisos && resaltada

    return (
        <button
      ref= { ref }
    type = "button"
    className = { estaResaltada? `${styles.campana} ${styles.resaltada}` : styles.campana
}
aria-label={ textoAccesible(cantidad, estaResaltada) }
aria-expanded={ abierto }
aria-controls={ abierto ? idPanel : undefined }
onClick = { onClick }
    >
    <svg
        className={ styles.icono }
viewBox = "0 0 24 24"
width = "26"
height = "26"
fill = "none"
stroke = "currentColor"
strokeWidth = "2"
strokeLinecap = "round"
strokeLinejoin = "round"
aria-hidden="true"
focusable = "false"
    >
    <path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" />
        <path d="M13.7 21a2 2 0 0 1-3.4 0" />
            </svg>
{
    hayAvisos && (
        <span className={ styles.contador } aria-hidden="true" >
        { cantidad > 99 ? '99+' : cantidad
}
</span>
      )}
</button>
  )
}