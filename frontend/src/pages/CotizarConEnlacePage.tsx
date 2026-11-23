import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { FormularioCotizacion } from '../licitaciones/FormularioCotizacion'
import type { CotizacionResponseDto } from '../licitaciones/licitacionesTipos'
import { cotizarConMagicLink, obtenerPliegoPorMagicLink } from '../licitaciones/magicLinkService'
import type { CotizarConMagicLinkRequestDto, PliegoMagicLinkDto } from '../licitaciones/magicLinkTipos'
import { textoVencimiento } from '../licitaciones/magicLinkTextos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import formulario from './CotizarLicitacionPage.module.css'
import styles from './CotizarConEnlacePage.module.css'

type Estado =
  | { tipo: 'cargando' }
  | { tipo: 'sinPantalla'; mensaje: string; codigo?: string; reintentable: boolean }
  | { tipo: 'listo'; pliego: PliegoMagicLinkDto }
  | { tipo: 'enviado'; pliego: PliegoMagicLinkDto; respuesta: CotizacionResponseDto }

const MENSAJE_NO_EXISTE = 'Este enlace no existe. Revisá que lo hayas copiado completo o pedile uno nuevo a Compras.'
const MENSAJE_USADO = 'Este enlace ya se usó. Cada enlace sirve una sola vez: pedile uno nuevo a Compras.'
const MENSAJE_VENCIDO = 'Este enlace venció o la licitación ya no recibe ofertas. Pedile uno nuevo a Compras.'
const MENSAJE_FALLA = 'No pudimos abrir el enlace ahora. Probá de nuevo en unos instantes.'

/**
 * Mensajes amables para quien abre un enlace que no sirve. No dicen más de lo necesario: el detalle técnico
 * queda en los logs del servidor, y el código (X-Correlation-ID) le permite a Compras rastrearlo.
 */
function describirErrorDeCarga(err: unknown): { mensaje: string; codigo?: string; reintentable: boolean } {
  if (err instanceof ApiError) {
    if (err.status === 404) return { mensaje: MENSAJE_NO_EXISTE, codigo: err.correlationId, reintentable: false }
    if (err.status === 409) return { mensaje: MENSAJE_USADO, codigo: err.correlationId, reintentable: false }
    if (err.status === 400) return { mensaje: MENSAJE_VENCIDO, codigo: err.correlationId, reintentable: false }
    return { mensaje: MENSAJE_FALLA, codigo: err.correlationId, reintentable: true }
  }
  return { mensaje: MENSAJE_FALLA, reintentable: true }
}

/**
 * Pantalla PÚBLICA donde el proveedor cotiza desde su Magic Link, sin iniciar sesión (#235).
 * Ruta: /cotizar/:token. El token viaja solo hacia la API: no se guarda ni se muestra.
 */
export function CotizarConEnlacePage() {
  const { token = '' } = useParams<{ token: string }>()
  const [estado, setEstado] = useState<Estado>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)
  const [enviando, setEnviando] = useState(false)
  const [bloqueado, setBloqueado] = useState(false)
  const [errorEnvio, setErrorEnvio] = useState<string | null>(null)
  const peticionEnVuelo = useRef(false)

  useEffect(() => {
    let cancelado = false
    async function cargar() {
      try {
        const pliego = await obtenerPliegoPorMagicLink(token)
        if (!cancelado) setEstado({ tipo: 'listo', pliego })
      } catch (err) {
        if (!cancelado) setEstado({ tipo: 'sinPantalla', ...describirErrorDeCarga(err) })
      }
    }
    void cargar()
    return () => {
      cancelado = true
    }
  }, [token, intento])

  const reintentar = useCallback(() => {
    setEstado({ tipo: 'cargando' })
    setIntento((n) => n + 1)
  }, [])

  async function enviar(pliego: PliegoMagicLinkDto, valores: CotizarConMagicLinkRequestDto) {
    if (peticionEnVuelo.current) return
    peticionEnVuelo.current = true
    setEnviando(true)
    setErrorEnvio(null)
    try {
      const respuesta = await cotizarConMagicLink(token, valores)
      setEstado({ tipo: 'enviado', pliego, respuesta })
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setErrorEnvio(MENSAJE_NO_EXISTE)
        setBloqueado(true)
      } else if (err instanceof ApiError && err.status === 409) {
        setErrorEnvio(MENSAJE_USADO)
        setBloqueado(true)
      } else if (err instanceof ApiError && err.status === 400) {
        // Un 400 puede ser un dato mal cargado (el enlace sigue sirviendo) o un enlace vencido (ya no sirve).
        const vencido = /venc|expir|cerrad|abiert/i.test(err.detail ?? '')
        setErrorEnvio(vencido ? MENSAJE_VENCIDO : (err.detail ?? 'Revisá los datos de la cotización e intentá de nuevo.'))
        setBloqueado(vencido)
      } else {
        setErrorEnvio('No pudimos enviar tu cotización. Intentá de nuevo en unos instantes.')
      }
    } finally {
      peticionEnVuelo.current = false
      setEnviando(false)
    }
  }

  return (
    <main className={styles.pagina}>
      <header className={styles.marca}>
        <img src="/pwa-512x512.png" alt="" className={styles.logo} />
        <span className={styles.nombreMarca}>ObraSync</span>
      </header>

      {estado.tipo === 'cargando' && <p role="status">Abriendo tu enlace…</p>}

      {estado.tipo === 'sinPantalla' && (
        <div className={formulario.tarjetaAviso} role="alert">
          <h1 className={styles.tituloAviso}>No pudimos abrir este enlace</h1>
          <p>{estado.mensaje}</p>
          {estado.codigo && (
            <p className={styles.codigo}>Si necesitás ayuda, pasale este código a Compras: {estado.codigo}</p>
          )}
          {estado.reintentable && (
            <button type="button" className={styles.botonReintentar} onClick={reintentar}>
              Reintentar
            </button>
          )}
        </div>
      )}

      {estado.tipo === 'listo' && (
        <>
          <h1 className={styles.titulo}>Cotizá {estado.pliego.numeroRequisicion}</h1>
          {estado.pliego.proveedorNombre && <p className={styles.saludo}>Hola, {estado.pliego.proveedorNombre}.</p>}
          <p className={styles.datos}>
            Podés cotizar hasta el {fechaLarga(estado.pliego.fechaLimiteCotizacion)}. Este enlace sirve una sola vez y
            vence {textoVencimiento(estado.pliego.fechaDeVencimientoEnlace)}.
          </p>
          <p className={styles.advertencia}>Revisá los precios antes de enviar: al enviar, el enlace queda usado.</p>

          {errorEnvio && (
            <div className={formulario.errorApi} role="alert">
              <p>{errorEnvio}</p>
            </div>
          )}

          <FormularioCotizacion
            items={estado.pliego.items}
            enviando={enviando}
            bloqueado={bloqueado}
            textoBoton="Enviar cotización"
            onEnviar={(valores) => void enviar(estado.pliego, valores)}
          />
        </>
      )}

      {estado.tipo === 'enviado' && (
        <div className={formulario.tarjetaConfirmacion} role="status">
          <h1 className={formulario.tituloExito}>¡Recibimos tu cotización!</h1>
          <p>
            Tu cotización para <strong>{estado.pliego.numeroRequisicion}</strong> quedó registrada. Compras la va a
            revisar. Ya podés cerrar esta página: el enlace no se puede volver a usar.
          </p>
          {(estado.respuesta.coberturaParcial ||
            estado.respuesta.items?.some((i) => i.coberturaParcial)) && (
            <div className={formulario.avisoCobertura} role="alert">
              <strong>Esto no cubre todo lo pedido.</strong>
              <p>Tu oferta se envió igual: Compras la ve marcada y decide al adjudicar.</p>
              <ul>
                {estado.pliego.items
                  .filter((it) => estado.respuesta.items?.find((i) => i.articuloId === it.articuloId)?.coberturaParcial)
                  .map((it) => (
                    <li key={it.articuloId}>{it.descripcion}</li>
                  ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </main>
  )
}
