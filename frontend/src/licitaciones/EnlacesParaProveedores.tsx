import { useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
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

function mensajeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return err.detail ?? 'Los datos del enlace no son válidos.'
    if (err.status === 403) return 'No tenés permiso para generar enlaces.'
    if (err.status === 404) return 'No encontramos la licitación. Recargá la pantalla.'
    if (err.status === 409) return 'La licitación ya no está abierta o venció el plazo. Recargá la pantalla.'
  }
  return 'No se pudo generar el enlace. Intentá nuevamente.'
}

/**
 * Compras le arma a un proveedor un enlace de un solo uso para cotizar sin tener usuario (Magic Link, #252).
 * Elige al proveedor, genera el enlace y lo copia para mandárselo por WhatsApp o mail.
 * La lista de proveedores se pide recién cuando se abre el panel.
 */
export function EnlacesParaProveedores({ licitacionId }: Props) {
  const [abierto, setAbierto] = useState(false)
  const [proveedores, setProveedores] = useState<Proveedor[] | null>(null)
  const [cargando, setCargando] = useState(false)
  const [errorLista, setErrorLista] = useState(false)
  const [proveedorId, setProveedorId] = useState('')
  const [generando, setGenerando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [enlace, setEnlace] = useState<EnlaceListo | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const campoEnlace = useRef<HTMLInputElement>(null)

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

  function elegirProveedor(id: string) {
    setProveedorId(id)
    // El enlace que se ve tiene que ser siempre el del proveedor elegido.
    setEnlace(null)
    setError(null)
    setAviso(null)
  }

  async function generar() {
    const proveedor = proveedores?.find((p) => p.id === proveedorId)
    if (!proveedor || generando) return
    setGenerando(true)
    setError(null)
    setAviso(null)
    setEnlace(null)
    try {
      const respuesta = await generarMagicLink(licitacionId, proveedor)
      setEnlace({
        proveedor,
        url: urlPublicaMagicLink(respuesta.token),
        venceA: respuesta.fechaDeVencimiento,
        vigencia: textoVigencia(respuesta.horasVigencia),
      })
    } catch (err) {
      setError(mensajeError(err))
    } finally {
      setGenerando(false)
    }
  }

  async function copiar() {
    if (!enlace) return
    try {
      await navigator.clipboard.writeText(enlace.url)
      setAviso('Copiamos el enlace: pegalo en el WhatsApp o el mail del proveedor.')
    } catch {
      campoEnlace.current?.select()
      setAviso('No pudimos copiarlo solo: dejamos el enlace seleccionado, copialo desde ahí.')
    }
  }

  return (
    <section className={styles.enlaces} aria-label="Enlaces para proveedores">
      <div className={styles.encabezado}>
        <h3 className={styles.titulo}>Enlaces para proveedores</h3>
        <p className={styles.subtitulo}>
          Mandale a un proveedor un enlace para que cotice sin usuario. Funciona una sola vez y vence solo.
        </p>
      </div>

      {!abierto ? (
        <button type="button" className={styles.boton} onClick={abrir}>
          Generar enlace para un proveedor
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
              <label htmlFor="enlace-proveedor" className={styles.etiqueta}>Proveedor</label>
              <div className={styles.fila}>
                <select
                  id="enlace-proveedor"
                  className={styles.selector}
                  value={proveedorId}
                  onChange={(e) => elegirProveedor(e.target.value)}
                >
                  <option value="">Elegí un proveedor</option>
                  {proveedores.map((p) => (
                    <option key={p.id} value={p.id}>{p.nombreCompleto}</option>
                  ))}
                </select>
                <button
                  type="button"
                  className={styles.botonPrincipal}
                  onClick={() => void generar()}
                  disabled={!proveedorId || generando}
                >
                  {generando ? 'Generando…' : 'Generar enlace'}
                </button>
              </div>
            </>
          )}

          {enlace && (
            <div className={styles.resultado}>
              <label htmlFor="enlace-generado" className={styles.etiqueta}>
                Enlace para {enlace.proveedor.nombreCompleto}
              </label>
              <div className={styles.fila}>
                <input
                  id="enlace-generado"
                  ref={campoEnlace}
                  type="text"
                  readOnly
                  className={styles.campoEnlace}
                  value={enlace.url}
                  onFocus={(e) => e.currentTarget.select()}
                />
                <button type="button" className={styles.botonPrincipal} onClick={() => void copiar()}>
                  Copiar enlace
                </button>
              </div>
              <p className={styles.ayuda}>
                Sirve una sola vez y vence {textoVencimiento(enlace.venceA)} (dura {enlace.vigencia}). Mandáselo solo a{' '}
                {enlace.proveedor.nombreCompleto}. Si generás otro enlace para el mismo proveedor, el anterior sigue
                funcionando hasta que se use o venza.
              </p>
            </div>
          )}

          {error && <p role="alert" className={styles.error}>{error}</p>}
          {aviso && <p role="status" className={styles.aviso}>{aviso}</p>}
        </div>
      )}
    </section>
  )
}
