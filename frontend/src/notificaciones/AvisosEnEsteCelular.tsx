import { useEffect, useState } from 'react'
import { activarPush, desactivarPush, iosInstalacionRequerida, leerSuscripcion, mensajeErrorPush, soportaPush } from './pushService'
import styles from '../pages/InicioPage.module.css'

export function AvisosEnEsteCelular() {
  const [soportado] = useState(soportaPush)
  const [permiso, setPermiso] = useState<NotificationPermission | 'unsupported'>(() => soportaPush() ? Notification.permission : 'unsupported')
  const [suscripto, setSuscripto] = useState(false)
  const [procesando, setProcesando] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let activo = true
    if (soportado) void leerSuscripcion().then((suscripcion) => { if (activo) setSuscripto(Boolean(suscripcion)) }).catch(() => undefined)
    return () => { activo = false }
  }, [soportado])

  async function ejecutar(accion: () => Promise<void>) {
    setProcesando(true); setError('')
    try { await accion() } catch (fallo) { setError(mensajeErrorPush(fallo)) }
    finally {
      if (soportado) { setPermiso(Notification.permission); void leerSuscripcion().then((s) => setSuscripto(Boolean(s))).catch(() => undefined) }
      setProcesando(false)
    }
  }

  return <section className={styles.pushCard} aria-labelledby="avisos-celular-titulo">
    <h2 id="avisos-celular-titulo">Avisos en este celular</h2>
    {!soportado ? <p>Este navegador o dispositivo no admite notificaciones push.</p> : <>
      {iosInstalacionRequerida() && <p>En iPhone, Web Push requiere iOS 16.4 o superior y ObraSync instalada en la pantalla de inicio. Abrí el menú de Safari y elegí “Agregar a pantalla de inicio”.</p>}
      {permiso === 'denied' ? <p>Bloqueaste los avisos. Habilitalos desde la configuración de permisos del navegador o del dispositivo.</p> : suscripto ? <>
        <p role="status">Los avisos están habilitados en este dispositivo.</p>
        <button type="button" disabled={procesando} onClick={() => void ejecutar(desactivarPush)}>{procesando ? 'Procesando…' : 'Desactivar avisos'}</button>
      </> : <>
        <p>{permiso === 'default' ? 'Todavía no activaste los avisos.' : 'El permiso está concedido, pero este dispositivo aún no está suscripto.'}</p>
        <button type="button" disabled={procesando} onClick={() => void ejecutar(activarPush)}>{procesando ? 'Procesando…' : 'Activar avisos'}</button>
      </>}
    </>}
    {error && <p role="alert">{error}</p>}
  </section>
}
