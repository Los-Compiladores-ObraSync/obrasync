import styles from './InsigniaCantidad.module.css'

/** Numerito naranja al lado de un texto (por ejemplo, cuántas requisiciones esperan). Con 0 no se muestra. */
export function InsigniaCantidad({
  cantidad,
  descripcion,
}: {
  cantidad: number
  /** Texto para lectores de pantalla: el número solo no dice a qué se refiere. */
  descripcion: string
}) {
  if (cantidad <= 0) return null
  return (
    <span className={styles.insignia} role="img" aria-label={descripcion}>
      {cantidad > 99 ? '99+' : cantidad}
    </span>
  )
}
