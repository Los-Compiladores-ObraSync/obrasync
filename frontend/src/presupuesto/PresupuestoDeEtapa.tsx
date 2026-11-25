import { useEffect, useState } from 'react'
import { obtenerPartidas } from './presupuestoService'
import type { PartidaPresupuestaria } from './presupuestoTipos'
import styles from './PresupuestoDeEtapa.module.css'

interface Props {
  obraId: string
  etapa?: string | null
  /** Costo estimado de la requisición (precios de referencia): se compara con lo que queda disponible. */
  estimado?: number
}

function pesos(valor: number): string {
  return `$ ${valor.toLocaleString('es-AR', { maximumFractionDigits: 0 })}`
}

/** Presupuesto asignado de la etapa y cuánto queda: lo primero que mira Compras al decidir. */
export function PresupuestoDeEtapa({ obraId, etapa, estimado }: Props) {
  const [partida, setPartida] = useState<PartidaPresupuestaria | null | undefined>(undefined)

  useEffect(() => {
    let cancelado = false
    obtenerPartidas({ obraId, etapa: etapa ?? undefined })
      .then((lista) => {
        if (cancelado) return
        const nombre = (etapa ?? '').trim().toLowerCase()
        const propia = lista.find((p) => (p.etapa ?? '').trim().toLowerCase() === nombre)
        setPartida(propia ?? lista.find((p) => p.etapa === null) ?? null)
      })
      .catch(() => {
        if (!cancelado) setPartida(null)
      })
    return () => {
      cancelado = true
    }
  }, [obraId, etapa])

  if (partida === undefined) return <p className={styles.cargando} role="status">Cargando presupuesto…</p>
  if (partida === null) {
    return <p className={styles.sinPartida}>Esta etapa todavía no tiene presupuesto asignado.</p>
  }

  const usado = partida.montoComprometido
  const porcentaje = partida.montoAsignado > 0 ? Math.min(100, Math.round((usado / partida.montoAsignado) * 100)) : 100
  const disponible = partida.montoDisponible
  const superaria = estimado !== undefined && estimado > 0 && estimado > disponible

  return (
    <section className={styles.bloque} aria-label="Presupuesto de la etapa">
      <p className={styles.titulo}>Presupuesto de la etapa{partida.etapa ? ` ${partida.etapa}` : ''}</p>
      <p className={styles.disponible}>
        Disponible: <strong>{pesos(disponible)}</strong> de {pesos(partida.montoAsignado)}
      </p>
      <div
        className={styles.barra}
        role="progressbar"
        aria-label="Presupuesto comprometido"
        aria-valuenow={porcentaje}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div className={styles.relleno} style={{ width: `${porcentaje}%` }} />
      </div>
      <p className={styles.detalle}>
        Comprometido {pesos(partida.montoComprometido)} · Ejecutado {pesos(partida.montoEjecutado)}
      </p>
      {estimado !== undefined && estimado > 0 && (
        <p className={superaria ? styles.alerta : styles.estimado} role={superaria ? 'alert' : undefined}>
          {superaria
            ? `Esta requisición estimada en ${pesos(estimado)} supera lo disponible: la orden de compra quedaría bloqueada.`
            : `Esta requisición, estimada en ${pesos(estimado)}, entra en el presupuesto.`}
        </p>
      )}
    </section>
  )
}
