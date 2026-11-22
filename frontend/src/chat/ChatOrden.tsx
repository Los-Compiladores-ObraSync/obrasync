import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { useAuth } from '../auth/useAuth'
import { MAX_MENSAJE } from './chatConfig'
import { fechaYHora, puedeResolverProblema } from './chatMensajes'
import type { EstadoProblema } from './chatTipos'
import styles from './ChatOrden.module.css'
import { useChatOrden } from './useChatOrden'

interface ChatOrdenProps {
  ordenCompraId: string
  /** Código corto de la orden, para los textos de accesibilidad ("Orden AB12CD34"). */
  codigo: string
  /** Mensajes sin leer de la orden (de la lista o de la consulta de no leídos). */
  noLeidos: number
  /** Cómo está la entrega con problema según la lista de órdenes. */
  problema: EstadoProblema
  /** Se llama al abrir y al leer: la lista deja en 0 el contador de la orden. */
  alLeer: () => void
  /** Se llama cuando cambia el problema (tiempo real, consulta o "Marcar como resuelto"), para que la tarjeta lo muestre. */
  alCambiarProblema: (estado: EstadoProblema) => void
}

/** Chat de una orden entre Compras y el Proveedor (#225): botón con contador de no leídos que despliega la conversación. */
export function ChatOrden({ ordenCompraId, codigo, noLeidos, problema, alLeer, alCambiarProblema }: ChatOrdenProps) {
  const [abierto, setAbierto] = useState(false)
  const idPanel = useId()

  function alternar() {
    setAbierto((actual) => !actual)
    // Al abrir, la conversación se marca como leída: el contador baja ya.
    if (!abierto) alLeer()
  }

  return (
    <div className={styles.contenedor}>
      <button
        type="button"
        className={styles.alternar}
        aria-expanded={abierto}
        aria-controls={idPanel}
        aria-label={
          abierto
            ? `Cerrar el chat de la orden ${codigo}`
            : noLeidos > 0
              ? `Abrir el chat de la orden ${codigo}, ${noLeidos} sin leer`
              : `Abrir el chat de la orden ${codigo}`
        }
        onClick={alternar}
      >
        {abierto ? 'Cerrar chat' : 'Chat'}
        {!abierto && noLeidos > 0 && (
          <span className={styles.contador} aria-hidden="true">
            {noLeidos > 99 ? '99+' : noLeidos}
          </span>
        )}
      </button>
      <div id={idPanel} hidden={!abierto}>
        {abierto && (
          <Conversacion
            ordenCompraId={ordenCompraId}
            codigo={codigo}
            problema={problema}
            alLeer={alLeer}
            alCambiarProblema={alCambiarProblema}
          />
        )}
      </div>
    </div>
  )
}

type ConversacionProps = Pick<ChatOrdenProps, 'ordenCompraId' | 'codigo' | 'problema' | 'alLeer' | 'alCambiarProblema'>

function Conversacion({ ordenCompraId, codigo, problema, alLeer, alCambiarProblema }: ConversacionProps) {
  const { usuario } = useAuth()
  const chat = useChatOrden({ ordenCompraId, problemaInicial: problema, alLeer })
  const [texto, setTexto] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [resolviendo, setResolviendo] = useState(false)
  const listaRef = useRef<HTMLUListElement>(null)

  // La tarjeta de la orden muestra el aviso de problema: se mantiene al día con lo que ve el chat.
  const { abierto: problemaAbierto, motivo: problemaMotivo } = chat.problema
  const alCambiarProblemaRef = useRef(alCambiarProblema)
  useEffect(() => {
    alCambiarProblemaRef.current = alCambiarProblema
  })
  useEffect(() => {
    alCambiarProblemaRef.current({ abierto: problemaAbierto, motivo: problemaMotivo })
  }, [problemaAbierto, problemaMotivo])

  // Siempre se ve lo último.
  const cantidad = chat.mensajes.length
  useEffect(() => {
    const lista = listaRef.current
    if (lista) lista.scrollTop = lista.scrollHeight
  }, [cantidad])

  async function enviar(e?: FormEvent) {
    e?.preventDefault()
    if (enviando) return
    setEnviando(true)
    const falla = await chat.enviar(texto)
    setEnviando(false)
    setError(falla)
    if (!falla) setTexto('')
  }

  // En la computadora, Enter envía y Shift+Enter hace un salto de línea. Mientras se compone con un teclado
  // de otro idioma (isComposing), Enter confirma la letra y no debe enviar.
  function alPresionarTecla(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      void enviar()
    }
  }

  async function resolver() {
    setResolviendo(true)
    const falla = await chat.resolver()
    setResolviendo(false)
    setError(falla)
  }

  const puedeResolver = puedeResolverProblema(usuario?.rol) && chat.problema.abierto

  return (
    <section className={styles.panel} aria-label={`Chat de la orden ${codigo}`}>
      {chat.enVivo !== 'en-vivo' && (
        <p className={styles.sinConexion} role="status">
          {chat.enVivo === 'conectando' ? 'Conectando…' : 'Sin conexión en vivo. Se actualiza cada 10 segundos.'}
        </p>
      )}

      {puedeResolver && (
        <button type="button" className={styles.resolver} disabled={resolviendo} onClick={() => void resolver()}>
          {resolviendo ? 'Guardando…' : 'Marcar como resuelto'}
        </button>
      )}

      {chat.cargando && <p className={styles.ayuda}>Cargando la conversación…</p>}

      {chat.errorCarga && (
        <div className={styles.errorCarga} role="alert">
          <p>{chat.errorCarga}</p>
          <button type="button" className={styles.reintentar} onClick={chat.reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!chat.cargando && !chat.errorCarga && chat.mensajes.length === 0 && (
        <p className={styles.ayuda}>Todavía no hay mensajes. Escribí el primero.</p>
      )}

      {chat.mensajes.length > 0 && (
        <ul ref={listaRef} className={styles.mensajes} aria-label="Mensajes">
          {chat.mensajes.map((m) => {
            if (m.esSistema) {
              return (
                <li key={m.id} className={styles.sistema} data-tipo="sistema">
                  <p className={styles.textoSistema}>{m.texto}</p>
                  <span className={styles.hora}>{fechaYHora(m.fechaCreacion)}</span>
                </li>
              )
            }
            const mio = m.autorId != null && m.autorId === usuario?.id
            const visto = mio && chat.vistoHasta !== null && Date.parse(m.fechaCreacion) <= Date.parse(chat.vistoHasta)
            return (
              <li key={m.id} className={`${styles.mensaje} ${mio ? styles.mio : styles.ajeno}`} data-tipo="persona">
                <span className={styles.autor}>{mio ? 'Vos' : `${m.autorNombre} · ${m.autorRol}`}</span>
                <p className={styles.texto}>{m.texto}</p>
                <span className={styles.hora}>
                  {fechaYHora(m.fechaCreacion)}
                  {visto ? ' · Visto' : ''}
                </span>
              </li>
            )
          })}
        </ul>
      )}

      <form className={styles.formulario} onSubmit={(e) => void enviar(e)} noValidate>
        <label htmlFor={`${ordenCompraId}-mensaje`} className={styles.etiqueta}>
          Mensaje
        </label>
        <textarea
          id={`${ordenCompraId}-mensaje`}
          className={styles.campo}
          rows={2}
          value={texto}
          maxLength={MAX_MENSAJE}
          placeholder="Escribí un mensaje…"
          onKeyDown={alPresionarTecla}
          onChange={(e) => {
            setTexto(e.target.value)
            if (error) setError(null)
          }}
        />
        <div className={styles.pie}>
          <span className={styles.contadorTexto}>
            {texto.length}/{MAX_MENSAJE}
          </span>
          <button type="submit" className={styles.enviar} disabled={enviando}>
            {enviando ? 'Enviando…' : 'Enviar'}
          </button>
        </div>
        {error && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}
      </form>
    </section>
  )
}
