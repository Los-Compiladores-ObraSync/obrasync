import { useState } from 'react'
import { ApiError } from '../api/ApiError'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { cerrarLicitacion, extenderLicitacion } from './licitacionesService'
import type { LicitacionResponseDto } from './licitacionesTipos'
import { finDelDiaEnUtc } from './plazoCotizacion'
import styles from './AccionesLicitacionAbierta.module.css'

interface Props {
  licitacion: LicitacionResponseDto
  /** Cuando una acción sale bien, la pantalla reemplaza la licitación por la que devolvió el backend. */
  onActualizada: (licitacion: LicitacionResponseDto) => void
}

const MAX_MOTIVO = 500

/** "YYYY-MM-DD" (hora local) del día de la fecha límite actual más `dias`. */
function fechaInputMasDias(iso: string, dias: number): string {
  const f = new Date(iso)
  f.setDate(f.getDate() + dias)
  const mes = String(f.getMonth() + 1).padStart(2, '0')
  const dia = String(f.getDate()).padStart(2, '0')
  return `${f.getFullYear()}-${mes}-${dia}`
}

function mensaje(err: unknown, accion: 'extender' | 'cerrar'): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return err.detail ?? 'Los datos no son válidos.'
    if (err.status === 409) return 'La licitación ya no está abierta. Recargá la pantalla.'
    if (err.status === 404) return 'No encontramos la licitación.'
  }
  return accion === 'extender'
    ? 'No se pudo extender el plazo. Intentá nuevamente.'
    : 'No se pudo cerrar la licitación. Intentá nuevamente.'
}

/**
 * Qué puede hacer Compras con una licitación que sigue abierta: darle más tiempo, cerrarla antes de la fecha
 * (sin adjudicar) o copiar un recordatorio para los proveedores que todavía no cotizaron.
 */
export function AccionesLicitacionAbierta({ licitacion, onActualizada }: Props) {
  const [fecha, setFecha] = useState(() => fechaInputMasDias(licitacion.fechaLimiteCotizacion, 3))
  const [cerrando, setCerrando] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)

  async function extender() {
    if (enviando) return
    setEnviando(true)
    setError(null)
    setAviso(null)
    try {
      const actualizada = await extenderLicitacion(licitacion.licitacionId, finDelDiaEnUtc(fecha))
      onActualizada(actualizada)
      setAviso(`Listo: los proveedores pueden cotizar hasta el ${fechaLarga(actualizada.fechaLimiteCotizacion)}.`)
    } catch (err) {
      setError(mensaje(err, 'extender'))
    } finally {
      setEnviando(false)
    }
  }

  async function cerrar() {
    if (enviando) return
    if (motivo.trim() === '') {
      setError('Contá por qué cerrás la licitación antes de tiempo.')
      return
    }
    setEnviando(true)
    setError(null)
    setAviso(null)
    try {
      const actualizada = await cerrarLicitacion(licitacion.licitacionId, motivo.trim())
      onActualizada(actualizada)
    } catch (err) {
      setError(mensaje(err, 'cerrar'))
    } finally {
      setEnviando(false)
    }
  }

  async function copiarRecordatorio() {
    const texto =
      `Recordatorio: la licitación de ${licitacion.numeroRequisicion ?? 'ObraSync'} cierra el ` +
      `${fechaLarga(licitacion.fechaLimiteCotizacion)}. Si todavía no cotizaste, entrá a ObraSync y mandanos tu oferta.`
    try {
      await navigator.clipboard.writeText(texto)
      setAviso('Copiamos el recordatorio: pegalo en el mensaje o el WhatsApp de los proveedores.')
    } catch {
      setAviso(texto)
    }
  }

  return (
    <section className={styles.acciones} aria-label="Acciones sobre la licitación abierta">
      <h3 className={styles.titulo}>Mientras está abierta</h3>

      <div className={styles.fila}>
        <label htmlFor="extender-fecha" className={styles.etiqueta}>Dar más tiempo hasta</label>
        <input
          id="extender-fecha"
          type="date"
          className={styles.fecha}
          value={fecha}
          onChange={(e) => setFecha(e.target.value)}
        />
        <button type="button" className={styles.boton} onClick={() => void extender()} disabled={enviando || !fecha}>
          Extender plazo
        </button>
      </div>

      <button type="button" className={styles.boton} onClick={() => void copiarRecordatorio()}>
        Copiar recordatorio para los proveedores
      </button>

      {!cerrando ? (
        <button type="button" className={styles.botonPeligro} onClick={() => setCerrando(true)}>
          Cerrar antes de tiempo
        </button>
      ) : (
        <div role="group" aria-label="Cerrar la licitación" className={styles.confirmar}>
          <label htmlFor="cerrar-motivo" className={styles.etiqueta}>Motivo del cierre</label>
          <input
            id="cerrar-motivo"
            type="text"
            className={styles.motivo}
            maxLength={MAX_MOTIVO}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <p className={styles.ayuda}>Los links que mandaste a los proveedores dejan de funcionar. No se puede deshacer.</p>
          <button type="button" className={styles.botonPeligro} onClick={() => void cerrar()} disabled={enviando}>
            Sí, cerrar la licitación
          </button>
          <button type="button" className={styles.boton} onClick={() => setCerrando(false)} disabled={enviando}>
            No, dejarla abierta
          </button>
        </div>
      )}

      {error && <p role="alert" className={styles.error}>{error}</p>}
      {aviso && <p role="status" className={styles.aviso}>{aviso}</p>}
    </section>
  )
}
