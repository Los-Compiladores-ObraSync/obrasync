import { useEffect, useId, useRef, useState } from 'react'
import { CampanaNotificaciones } from './CampanaNotificaciones'
import styles from './CampanaConPendientes.module.css'
import { UMBRAL_PENDIENTES } from './configuracionAvisos'
import { PanelNotificaciones } from './PanelNotificaciones'
import { useAvisos } from './useAvisos'
import { useBloqueadas } from './useBloqueadas'
import { useNovedades } from './useNovedades'
import { usePendientes } from './usePendientes'

/** La campana del encabezado conectada a la cantidad real de pendientes, de órdenes bloqueadas y de novedades del Capataz y el Proveedor (#163),
 *  con el panel de avisos que se abre al tocarla (#49, #97). */
export function CampanaConPendientes() {
  const { cantidad: cantidadPendientes, pendientes } = usePendientes()
  const { cantidad: cantidadBloqueadas, cargado: bloqueadasCargadas, huboError, refrescar } = useBloqueadas()
  const { cantidad: cantidadNovedades, novedades, marcarVistas } = useNovedades()
  const { cantidad: cantidadAvisos, paraHacer, novedades: novedadesDeFlujo, marcarVistas: marcarAvisos } = useAvisos()
  const cantidadTotal = cantidadPendientes + cantidadBloqueadas + cantidadNovedades + cantidadAvisos
  const [abierto, setAbierto] = useState(false)
  const idPanel = useId()
  const contenedorRef = useRef<HTMLDivElement>(null)
  const botonRef = useRef<HTMLButtonElement>(null)

  // Mientras el panel está abierto escuchamos dos cosas: Escape (cierra y le
  // devuelve el foco a la campana) y un toque afuera (cierra, sin mover el foco:
  // la persona ya eligió dónde quiere ir). Al cerrar, se quitan los listeners.
  useEffect(() => {
    if (!abierto) return

    function alTeclear(evento: KeyboardEvent) {
      if (evento.key !== 'Escape') return
      setAbierto(false)
      botonRef.current?.focus()
    }

    function alTocar(evento: PointerEvent) {
      if (!contenedorRef.current?.contains(evento.target as Node)) {
        setAbierto(false)
      }
    }

    document.addEventListener('keydown', alTeclear)
    document.addEventListener('pointerdown', alTocar)
    return () => {
      document.removeEventListener('keydown', alTeclear)
      document.removeEventListener('pointerdown', alTocar)
    }
  }, [abierto])

  return (
    <div ref={contenedorRef} className={styles.contenedor}>
      <CampanaNotificaciones
        ref={botonRef}
        cantidad={cantidadTotal}
        errorAvisos={huboError}
        resaltada={cantidadTotal >= UMBRAL_PENDIENTES}
        abierto={abierto}
        idPanel={idPanel}
        onClick={() => setAbierto((anterior) => !anterior)}
      />
      {abierto && (
        <PanelNotificaciones
          id={idPanel}
          cantidadPendientes={cantidadPendientes}
          pendientes={pendientes}
          cantidadBloqueadas={cantidadBloqueadas}
          bloqueadasCargadas={bloqueadasCargadas}
          huboErrorBloqueadas={huboError}
          onReintentarBloqueadas={refrescar}
          novedades={novedades}
          onMarcarNovedades={marcarVistas}
          avisosParaHacer={paraHacer}
          avisosNovedades={novedadesDeFlujo}
          onMarcarAvisos={marcarAvisos}
          onNavegar={() => setAbierto(false)}
        />
      )}
    </div>
  )
}
