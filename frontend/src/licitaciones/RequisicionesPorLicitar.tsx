import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { traerTodas } from '../api/paginacion'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import styles from './RequisicionesPorLicitar.module.css'
import type { LicitacionResponseDto } from './licitacionesTipos'
import { listarLicitaciones } from './licitacionesService'

/** Una requisición con una licitación en estos estados ya no espera que la publiquen. */
const ESTADOS_YA_LICITADA = ['Abierta', 'Adjudicada']
const MAXIMO_VISIBLES = 5

/**
 * Aviso para el Comprador: requisiciones ya aprobadas a las que todavía no se les publicó la licitación.
 * Hasta que se publica, los proveedores no pueden cotizar, y antes había que adivinar que se hacía
 * desde el detalle de la requisición (Historial). Cada fila lleva directo a ese detalle.
 * Si no hay nada por publicar, o no se pudo consultar, no muestra nada (es un aviso, no una pantalla).
 */
export function RequisicionesPorLicitar({
  licitacionesConocidas,
}: {
  /** Si la pantalla ya tiene la lista de licitaciones, se la pasa y acá no se vuelve a pedir. */
  licitacionesConocidas?: LicitacionResponseDto[]
}) {
  const [porLicitar, setPorLicitar] = useState<RequisicionResponseDto[]>([])

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [aprobadas, licitaciones] = await Promise.all([
          traerTodas((pagina) => listarRequisiciones({ estado: 'Aprobada' }, pagina)),
          licitacionesConocidas ?? listarLicitaciones(),
        ])
        if (cancelado) return
        const yaLicitadas = new Set(
          licitaciones
            .filter((l) => ESTADOS_YA_LICITADA.includes(l.estado))
            .map((l) => l.requisicionId),
        )
        setPorLicitar(aprobadas.filter((r) => !yaLicitadas.has(r.requisicionId)))
      } catch {
        // Aviso opcional: si no se pudo consultar, la pantalla sigue funcionando sin él.
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [licitacionesConocidas])

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
