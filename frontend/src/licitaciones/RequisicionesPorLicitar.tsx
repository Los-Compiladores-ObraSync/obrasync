import { Link } from 'react-router'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import type { LicitacionResponseDto } from './licitacionesTipos'
import styles from './RequisicionesPorLicitar.module.css'
import { useRequisicionesPorLicitar } from './useRequisicionesPorLicitar'

const MAXIMO_VISIBLES = 5

/**
 * Aviso para el Comprador: requisiciones ya aprobadas a las que todavía no se les publicó la licitación.
 * Hasta que se publica, los proveedores no pueden cotizar, y antes había que adivinar que se hacía
 * desde el detalle de la requisición (Historial). Cada fila lleva directo a ese detalle.
 * Sin requisiciones por licitar no muestra nada (es un aviso, no una pantalla).
 */
export function RequisicionesPorLicitar({
  porLicitar,
}: {
  porLicitar: RequisicionResponseDto[]
}) {
  if (porLicitar.length === 0) return null

  const visibles = porLicitar.slice(0, MAXIMO_VISIBLES)
  const restantes = porLicitar.length - visibles.length

  return (
    <section className={styles.tarjeta} aria-label="Requisiciones listas para licitar">
      <p className={styles.titulo}>
        {porLicitar.length === 1
          ? '1 requisición aprobada espera que publiques su licitación'
          : `${porLicitar.length} requisiciones aprobadas esperan que publiques su licitación`}
      </p>
      <p className={styles.dato}>Hasta que la publiques, los proveedores no pueden cotizar.</p>
      <ul className={styles.lista}>
        {visibles.map((req) => (
          <li key={req.requisicionId} className={styles.fila}>
            <span className={styles.numero}>{req.numeroRequisicion}</span>
            <Link
              to={`/compras/requisiciones/historial/${req.requisicionId}`}
              className={styles.enlace}
              aria-label={`Publicar la licitación de ${req.numeroRequisicion}`}
            >
              Publicar licitación
            </Link>
          </li>
        ))}
      </ul>
      {restantes > 0 && (
        <p className={styles.dato}>
          Y {restantes} más: las encontrás en el Historial de requisiciones.
        </p>
      )}
    </section>
  )
}

/** Versión para pantallas que ya tienen la lista de licitaciones: la reutiliza en vez de pedirla de nuevo. */
export function AvisoPorLicitar({ licitaciones }: { licitaciones: LicitacionResponseDto[] }) {
  const porLicitar = useRequisicionesPorLicitar(licitaciones)
  return <RequisicionesPorLicitar porLicitar={porLicitar} />
}
