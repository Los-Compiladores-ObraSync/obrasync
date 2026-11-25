import { useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { digitosParaWhatsApp, urlWhatsApp } from '../auth/telefono'
import { generarMagicLink } from './licitacionesService'
import { textoVencimiento, textoVigencia } from './magicLinkTextos'
import { urlPublicaMagicLink } from './magicLinkRutas'
import { obtenerProveedores, type Proveedor } from './proveedoresService'
import styles from './EnlacesParaProveedores.module.css'

interface Props {
  licitacionId: string
}

interface EnlaceListo {
  proveedor: Proveedor
  url: string
  venceA: string
  vigencia: string
}

/** El mensaje que le llega al proveedor por WhatsApp, con el enlace listo para tocar. */
function mensajeWhatsApp(enlace: EnlaceListo): string {
  return `Hola ${enlace.proveedor.nombreCompleto}, te invitamos a cotizar un pedido en ObraSync. ` +
    `Entrá desde este enlace (sirve una sola vez y vence ${textoVencimiento(enlace.venceA)}): ${enlace.url}`
}

function mensajeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return err.detail ?? 'Los datos del enlace no son válidos.'
    if (err.status === 403) return 'No tenés permiso para generar enlaces.'
    if (err.status === 404) return 'No encontramos la licitación. Recargá la pantalla.'
    if (err.status === 409) return 'La licitación ya no está abierta o venció el plazo. Recargá la pantalla.'
  }
  return 'No se pudo generar el enlace. Intentá nuevamente.'
}

interface FallaEnlace {
  proveedor: Proveedor
  mensaje: string
}

/**
 * Compras arma enlaces de un solo uso para que los proveedores coticen sin tener usuario (Magic Link, #252).
 * Puede elegir uno o varios proveedores: cada uno recibe SU enlace (un enlace es de un solo uso, así que no se
 * comparte entre varios). Con más cotizaciones, Compras tiene más opciones para comparar.
 * La lista de proveedores se pide recién cuando se abre el panel.
 */
export function EnlacesParaProveedores({ licitacionId }: Props) {
  const [abierto, setAbierto] = useState(false)
  const [proveedores, setProveedores] = useState<Proveedor[] | null>(null)
  const [cargando, setCargando] = useState(false)
  const [errorLista, setErrorLista] = useState(false)
  const [seleccionados, setSeleccionados] = useState<string[]>([])
  const [generando, setGenerando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [enlaces, setEnlaces] = useState<EnlaceListo[]>([])
  const [fallas, setFallas] = useState<FallaEnlace[]>([])
  const [aviso, setAviso] = useState<string | null>(null)
  const camposEnlace = useRef<Record<string, HTMLInputElement | null>>({})

  async function cargarProveedores() {
    setCargando(true)
    setErrorLista(false)
    try {
      setProveedores(await obtenerProveedores())
    } catch {
      setErrorLista(true)
    } finally {
      setCargando(false)
    }
  }

  function abrir() {
    setAbierto(true)
    if (proveedores === null) void cargarProveedores()
  }

  function alternar(id: string) {
    setSeleccionados((actuales) => (actuales.includes(id) ? actuales.filter((x) => x !== id) : [...actuales, id]))
  }

  function alternarTodos() {
    if (!proveedores) return
    setSeleccionados((actuales) => (actuales.length === proveedores.length ? [] : proveedores.map((p) => p.id)))
  }

  async function generar() {
    const elegidos = (proveedores ?? []).filter((p) => seleccionados.includes(p.id))
    if (elegidos.length === 0 || generando) return
    setGenerando(true)
    setError(null)
    setAviso(null)
    setEnlaces([])
    setFallas([])
    const listos: EnlaceListo[] = []
    const conFalla: FallaEnlace[] = []
    // Uno por uno: es parejo con el servidor y el orden de la lista queda igual al que se eligió.
    for (const proveedor of elegidos) {
      try {
        const respuesta = await generarMagicLink(licitacionId, proveedor)
        listos.push({
          proveedor,
          url: urlPublicaMagicLink(respuesta.token),
          venceA: respuesta.fechaDeVencimiento,
          vigencia: textoVigencia(respuesta.horasVigencia),
        })
      } catch (err) {
        // 404 y 409 son de la licitación (no existe, ya no está abierta): se repetirían con cada proveedor.
        if (err instanceof ApiError && (err.status === 404 || err.status === 409)) {
          setError(mensajeError(err))
          break
        }
        conFalla.push({ proveedor, mensaje: mensajeError(err) })
      }
    }
    setEnlaces(listos)
    setFallas(conFalla)
    // Con un solo proveedor se dispara solo la app de WhatsApp con el mensaje y el enlace escritos: falta tocar "Enviar".
    // Con varios no: el navegador deja abrir una sola ventana por toque, así que cada fila tiene su botón.
    if (elegidos.length === 1 && listos.length === 1) {
      const abierta = window.open(urlWhatsApp(listos[0].proveedor.telefono, mensajeWhatsApp(listos[0])), '_blank', 'noopener')
      if (abierta === null) setAviso('Tocá "Enviar por WhatsApp" para abrirlo con el mensaje listo.')
    } else if (listos.length > 1) {
      setAviso('Cada proveedor tiene su propio enlace. Tocá "Enviar por WhatsApp" en cada fila para mandárselo.')
    }
    setGenerando(false)
  }

  async function copiar(enlace: EnlaceListo) {
    try {
      await navigator.clipboard.writeText(enlace.url)
      setAviso(`Copiamos el enlace de ${enlace.proveedor.nombreCompleto}: pegalo en su WhatsApp o su mail.`)
    } catch {
      camposEnlace.current[enlace.proveedor.id]?.select()
      setAviso('No pudimos copiarlo solo: dejamos el enlace seleccionado, copialo desde ahí.')
    }
  }

  const cantidad = seleccionados.length
  const textoGenerar = generando ? 'Generando…' : cantidad > 1 ? `Generar ${cantidad} enlaces` : 'Generar enlace'

  return (
    <section className={styles.enlaces} aria-label="Enlaces para proveedores">
      <div className={styles.encabezado}>
        <h3 className={styles.titulo}>Enlaces para proveedores</h3>
        <p className={styles.subtitulo}>
          Mandale a uno o a varios proveedores un enlace para que coticen sin usuario. Cada enlace es de un proveedor,
          funciona una sola vez y vence solo.
        </p>
      </div>

      {!abierto ? (
        <button type="button" className={styles.boton} onClick={abrir}>
          Generar enlaces para proveedores
        </button>
      ) : (
        <div className={styles.panel}>
          {cargando && <p role="status" className={styles.ayuda}>Cargando proveedores…</p>}

          {errorLista && (
            <div className={styles.fila}>
              <p role="alert" className={styles.error}>No pudimos cargar los proveedores.</p>
              <button type="button" className={styles.boton} onClick={() => void cargarProveedores()}>
                Reintentar
              </button>
            </div>
          )}

          {proveedores !== null && proveedores.length === 0 && (
            <p className={styles.ayuda}>No hay proveedores activos para invitar.</p>
          )}

          {proveedores !== null && proveedores.length > 0 && (
            <>
              <fieldset className={styles.grupoProveedores}>
                <legend className={styles.etiqueta}>Proveedores</legend>
                <ul className={styles.listaProveedores}>
                  {proveedores.map((p) => (
                    <li key={p.id}>
                      <label className={styles.opcion}>
                        <input
                          type="checkbox"
                          checked={seleccionados.includes(p.id)}
                          onChange={() => alternar(p.id)}
                        />
                        <span>{p.nombreCompleto}</span>
                      </label>
                    </li>
                  ))}
                </ul>
              </fieldset>
              <div className={styles.fila}>
                <button type="button" className={styles.boton} onClick={alternarTodos}>
                  {cantidad === proveedores.length ? 'Quitar la selección' : 'Elegir todos'}
                </button>
                <button
                  type="button"
                  className={styles.botonPrincipal}
                  onClick={() => void generar()}
                  disabled={cantidad === 0 || generando}
                >
                  {textoGenerar}
                </button>
              </div>
            </>
          )}

          {enlaces.length > 0 && (
            <ul className={styles.listaEnlaces} aria-label="Enlaces generados">
              {enlaces.map((enlace) => {
                const idCampo = `enlace-generado-${enlace.proveedor.id}`
                const nombre = enlace.proveedor.nombreCompleto
                return (
                  <li key={enlace.proveedor.id} className={styles.resultado}>
                    <label htmlFor={idCampo} className={styles.etiqueta}>
                      Enlace para {nombre}
                    </label>
                    <div className={styles.fila}>
                      <input
                        id={idCampo}
                        ref={(el) => { camposEnlace.current[enlace.proveedor.id] = el }}
                        type="text"
                        readOnly
                        className={styles.campoEnlace}
                        value={enlace.url}
                        onFocus={(e) => e.currentTarget.select()}
                      />
                      <button
                        type="button"
                        className={styles.botonPrincipal}
                        aria-label={`Copiar enlace de ${nombre}`}
                        onClick={() => void copiar(enlace)}
                      >
                        Copiar enlace
                      </button>
                      <a
                        className={styles.botonWhatsApp}
                        aria-label={`Enviar por WhatsApp a ${nombre}`}
                        href={urlWhatsApp(enlace.proveedor.telefono, mensajeWhatsApp(enlace))}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        Enviar por WhatsApp
                      </a>
                    </div>
                    {digitosParaWhatsApp(enlace.proveedor.telefono) === null && (
                      <p className={styles.ayuda}>
                        {nombre} no tiene un teléfono con código de país cargado: WhatsApp te va a pedir que elijas el
                        contacto.
                      </p>
                    )}
                    <p className={styles.ayuda}>
                      Sirve una sola vez y vence {textoVencimiento(enlace.venceA)} (dura {enlace.vigencia}). Mandáselo
                      solo a {nombre}.
                    </p>
                  </li>
                )
              })}
            </ul>
          )}
          {enlaces.length > 0 && (
            <p className={styles.ayuda}>
              Si generás otro enlace para el mismo proveedor, el anterior sigue funcionando hasta que se use o venza.
            </p>
          )}

          {fallas.map((falla) => (
            <p key={falla.proveedor.id} role="alert" className={styles.error}>
              No pudimos generar el enlace de {falla.proveedor.nombreCompleto}. {falla.mensaje}
            </p>
          ))}
          {error && <p role="alert" className={styles.error}>{error}</p>}
          {aviso && <p role="status" className={styles.aviso}>{aviso}</p>}
        </div>
      )}
    </section>
  )
}
