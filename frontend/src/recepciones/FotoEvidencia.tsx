import { useEffect, useState } from 'react'
import { descargarFotoEvidencia } from './recepcionesService'
import type { FotoRecepcion } from './recepcionesTipos'
import styles from './FotoEvidencia.module.css'

interface FotoEvidenciaProps {
  recepcionId: string
  foto: FotoRecepcion
  /** 1, 2 o 3: para nombrarla ("Foto 1"). */
  numero: number
}

type Estado = { tipo: 'cargando' } | { tipo: 'lista'; url: string } | { tipo: 'error' }

/**
 * Una foto de evidencia de una entrega con problema (#224). La ruta de la imagen exige sesión, así que no se puede
 * poner directo en un <img>: se baja con el token y se muestra desde el navegador. Tocarla la agranda.
 */
export function FotoEvidencia({ recepcionId, foto, numero }: FotoEvidenciaProps) {
  const [estado, setEstado] = useState<Estado>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)
  const [ampliada, setAmpliada] = useState(false)

  useEffect(() => {
    let cancelado = false
    let url: string | null = null
    descargarFotoEvidencia(recepcionId, foto.id)
      .then((imagen) => {
        if (cancelado) return
        url = URL.createObjectURL(imagen)
        setEstado({ tipo: 'lista', url })
      })
      .catch(() => {
        if (!cancelado) setEstado({ tipo: 'error' })
      })
    return () => {
      cancelado = true
      // La URL vive en la memoria del navegador: se libera al salir de la pantalla o al bajar otra vez.
      if (url) URL.revokeObjectURL(url)
    }
  }, [recepcionId, foto.id, intento])

  // Con la foto ampliada, Escape la cierra.
  useEffect(() => {
    if (!ampliada) return
    function alTocarTecla(e: KeyboardEvent) {
      if (e.key === 'Escape') setAmpliada(false)
    }
    document.addEventListener('keydown', alTocarTecla)
    return () => document.removeEventListener('keydown', alTocarTecla)
  }, [ampliada])

  function reintentar() {
    setEstado({ tipo: 'cargando' })
    setIntento((n) => n + 1)
  }

  if (estado.tipo === 'cargando') {
    return (
      <span className={styles.estado} role="status">
        Cargando foto {numero}…
      </span>
    )
  }

  if (estado.tipo === 'error') {
    return (
      <span className={styles.error}>
        <span role="alert">No se pudo cargar la foto {numero}.</span>
        <button type="button" className={styles.reintentar} onClick={reintentar}>
          Reintentar foto {numero}
        </button>
      </span>
    )
  }

  return (
    <>
      <button type="button" className={styles.miniatura} onClick={() => setAmpliada(true)} aria-label={`Ampliar foto ${numero}`}>
        <img src={estado.url} alt={`Foto ${numero} de la entrega con problema`} className={styles.imagen} />
      </button>
      {ampliada && (
        <div className={styles.fondo} role="dialog" aria-modal="true" aria-label={`Foto ${numero} ampliada`}>
          <img src={estado.url} alt={`Foto ${numero} ampliada`} className={styles.ampliada} />
          <button type="button" className={styles.cerrar} onClick={() => setAmpliada(false)} autoFocus>
            Cerrar foto
          </button>
        </div>
      )}
    </>
  )
}
