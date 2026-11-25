import { type ChangeEvent, type FormEvent, useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { MAX_FOTOS_EVIDENCIA, reducirFotoEvidencia } from './imagenEvidencia'
import { ErrorDeImagen } from './imagenRemito'
import { registrarEntregaConProblema } from './recepcionesService'
import { MOTIVOS_PROBLEMA, type MotivoProblema, type OrdenPendiente, type RecepcionRegistrada } from './recepcionesTipos'
import {
  armarDetalleProblema,
  esMotivoProblema,
  MOTIVO_INCOMPLETA,
  type AlcanceIncompleta,
  hayErroresProblema,
  LARGO_MAXIMO_DETALLE_PROBLEMA,
  validarEntregaConProblema,
  type ErroresProblema,
} from './validacionEntregaConProblema'
import styles from './EntregaConProblemaForm.module.css'

interface EntregaConProblemaFormProps {
  orden: OrdenPendiente
  /** Se llama con la entrega ya registrada. */
  onRegistrada: (registrada: RecepcionRegistrada) => void
  /** "Volver" sin registrar nada. */
  onCancelar: () => void
}

/** Una foto ya achicada, lista para subir, con su vista previa. */
interface FotoElegida {
  id: number
  imagen: Blob
  vistaPrevia: string
}

function mensajeDeErrorApi(err: unknown): string {
  if (err instanceof ApiError) {
    // El servidor ya manda el motivo en español (orden bloqueada, sin despachar, ya recibida…).
    if (err.status === 400 || err.status === 409) return err.detail ?? 'No se pudo registrar el problema. Revisá los datos.'
    if (err.status === 403) return 'No tenés permiso para registrar problemas en esta obra.'
    if (err.status === 413) return 'Las fotos pesan demasiado. Quitá alguna y probá de nuevo.'
    if (err.status === 503) return 'No se pudo verificar la obra en este momento. Probá de nuevo en unos segundos.'
  }
  return 'No se pudo conectar con el servidor. Tus fotos siguen acá: intentá nuevamente.'
}

/**
 * "Hubo un problema con la entrega" (#224): el Capataz elige qué pasó (no llegó nada, llegó incompleta, llegó dañada o
 * no es lo pedido), cuenta el detalle y adjunta hasta 3 fotos. No cambia lo que ya se recibió: la orden sigue esperando.
 */
export function EntregaConProblemaForm({ orden, onRegistrada, onCancelar }: EntregaConProblemaFormProps) {
  const [motivo, setMotivo] = useState<MotivoProblema | ''>('')
  const [detalle, setDetalle] = useState('')
  const [alcance, setAlcance] = useState<AlcanceIncompleta>('')
  const [marcados, setMarcados] = useState<string[]>([])
  const [fotos, setFotos] = useState<FotoElegida[]>([])
  const [preparando, setPreparando] = useState(false)
  const [errorFoto, setErrorFoto] = useState<string | null>(null)
  const [errores, setErrores] = useState<ErroresProblema>({})
  const [errorApi, setErrorApi] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato contra el doble toque: `enviando` recién cambia en el próximo render.
  const enviandoRef = useRef(false)
  const siguienteId = useRef(0)
  const entradaCamaraRef = useRef<HTMLInputElement>(null)
  const entradaGaleriaRef = useRef<HTMLInputElement>(null)

  // Las vistas previas viven en la memoria del navegador: se liberan al salir.
  const fotosRef = useRef<FotoElegida[]>([])
  useEffect(() => {
    fotosRef.current = fotos
  })
  useEffect(() => {
    return () => fotosRef.current.forEach((foto) => URL.revokeObjectURL(foto.vistaPrevia))
  }, [])

  async function agregarFotos(e: ChangeEvent<HTMLInputElement>) {
    const archivos = Array.from(e.target.files ?? [])
    // Se vacía el campo para poder elegir la misma foto otra vez.
    e.target.value = ''
    if (archivos.length === 0 || preparando) return

    const lugar = MAX_FOTOS_EVIDENCIA - fotos.length
    if (lugar <= 0) {
      setErrorFoto(`Ya agregaste las ${MAX_FOTOS_EVIDENCIA} fotos. Quitá una para poner otra.`)
      return
    }
    setErrorFoto(
      archivos.length > lugar ? `Solo entran ${MAX_FOTOS_EVIDENCIA} fotos: agregué las primeras ${lugar}.` : null,
    )
    setErrores((previos) => ({ ...previos, fotos: undefined }))
    setPreparando(true)
    for (const archivo of archivos.slice(0, lugar)) {
      try {
        const imagen = await reducirFotoEvidencia(archivo)
        // El id y la vista previa se calculan ACÁ y no dentro de setFotos: esa función corre después, cuando el
        // contador ya subió, y dos fotos elegidas juntas quedaban con el mismo id (quitar una borraba las dos).
        siguienteId.current += 1
        const id = siguienteId.current
        const vistaPrevia = URL.createObjectURL(imagen)
        setFotos((previas) => [...previas, { id, imagen, vistaPrevia }])
      } catch (err) {
        setErrorFoto(err instanceof ErrorDeImagen ? err.message : 'No pude preparar la foto. Probá con otra.')
      }
    }
    setPreparando(false)
  }

  function quitarFoto(id: number) {
    const foto = fotos.find((f) => f.id === id)
    if (foto) URL.revokeObjectURL(foto.vistaPrevia)
    setFotos((previas) => previas.filter((f) => f.id !== id))
    setErrorFoto(null)
  }

  /** Descripciones de los materiales marcados, en el orden de la orden. */
  function materialesMarcados(): string[] {
    return orden.items.filter((i) => marcados.includes(i.articuloId)).map((i) => i.descripcionArticulo)
  }

  function alternarMaterial(articuloId: string) {
    setMarcados((previos) =>
      previos.includes(articuloId) ? previos.filter((id) => id !== articuloId) : [...previos, articuloId],
    )
    setErrores((previos) => ({ ...previos, alcance: undefined, detalle: undefined }))
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current || preparando) return

    const datos = {
      motivo,
      detalle,
      cantidadFotos: fotos.length,
      alcanceIncompleta: alcance,
      materialesIncompletos: materialesMarcados(),
    }
    const nuevosErrores = validarEntregaConProblema(datos)
    setErrores(nuevosErrores)
    if (hayErroresProblema(nuevosErrores) || motivo === '') return

    setErrorApi(null)
    enviandoRef.current = true
    setEnviando(true)
    try {
      const registrada = await registrarEntregaConProblema(
        { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo, detalle: armarDetalleProblema(datos) || null },
        fotos.map((foto) => foto.imagen),
      )
      onRegistrada(registrada)
    } catch (err) {
      // Las fotos quedan elegidas: con mala señal no hay que volver a sacarlas.
      setErrorApi(mensajeDeErrorApi(err))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return (
    <form className={styles.formulario} onSubmit={handleSubmit} noValidate aria-label="Entrega con problema">
      <p className={styles.aclaracion}>
        Contanos qué pasó. No cambia lo que ya recibiste: la orden sigue esperando la mercadería.
      </p>

      {errorApi && (
        <p className={styles.errorApi} role="alert">
          {errorApi}
        </p>
      )}

      <fieldset className={styles.motivos} aria-describedby={errores.motivo ? 'problema-motivo-error' : undefined}>
        <legend className={styles.label}>¿Qué pasó con la entrega?</legend>
        {MOTIVOS_PROBLEMA.map((opcion) => (
          <label key={opcion} className={`${styles.motivo}${motivo === opcion ? ` ${styles.motivoElegido}` : ''}`}>
            <input
              type="radio"
              name="motivo-problema"
              value={opcion}
              checked={motivo === opcion}
              onChange={(e) => {
                if (esMotivoProblema(e.target.value)) setMotivo(e.target.value)
                setErrores((previos) => ({ ...previos, motivo: undefined, alcance: undefined }))
              }}
            />
            <span>{opcion}</span>
          </label>
        ))}
        {errores.motivo && (
          <p id="problema-motivo-error" className={styles.errorCampo} role="alert">
            {errores.motivo}
          </p>
        )}
      </fieldset>

      {motivo === MOTIVO_INCOMPLETA && (
        <fieldset className={styles.motivos} aria-describedby={errores.alcance ? 'problema-alcance-error' : undefined}>
          <legend className={styles.label}>¿Todo llegó incompleto o algo específico?</legend>
          <label className={`${styles.motivo}${alcance === 'todo' ? ` ${styles.motivoElegido}` : ''}`}>
            <input
              type="radio"
              name="alcance-incompleta"
              checked={alcance === 'todo'}
              onChange={() => {
                setAlcance('todo')
                setErrores((previos) => ({ ...previos, alcance: undefined, detalle: undefined }))
              }}
            />
            <span>Todo llegó incompleto</span>
          </label>
          <label className={`${styles.motivo}${alcance === 'algo' ? ` ${styles.motivoElegido}` : ''}`}>
            <input
              type="radio"
              name="alcance-incompleta"
              checked={alcance === 'algo'}
              onChange={() => {
                setAlcance('algo')
                setErrores((previos) => ({ ...previos, alcance: undefined, detalle: undefined }))
              }}
            />
            <span>Algo específico</span>
          </label>
          {alcance === 'algo' && (
            <ul className={styles.materiales} aria-label="Materiales que llegaron incompletos">
              {orden.items.map((item) => (
                <li key={item.articuloId}>
                  <label className={`${styles.motivo}${marcados.includes(item.articuloId) ? ` ${styles.motivoElegido}` : ''}`}>
                    <input
                      type="checkbox"
                      checked={marcados.includes(item.articuloId)}
                      onChange={() => alternarMaterial(item.articuloId)}
                    />
                    <span>{item.descripcionArticulo}</span>
                  </label>
                </li>
              ))}
            </ul>
          )}
          {errores.alcance && (
            <p id="problema-alcance-error" className={styles.errorCampo} role="alert">
              {errores.alcance}
            </p>
          )}
        </fieldset>
      )}

      <div className={styles.grupo}>
        <label htmlFor="problema-detalle" className={styles.label}>
          Detalle (opcional)
        </label>
        <textarea
          id="problema-detalle"
          className={`${styles.input} ${styles.textarea}${errores.detalle ? ` ${styles.inputInvalido}` : ''}`}
          value={detalle}
          onChange={(e) => setDetalle(e.target.value)}
          maxLength={LARGO_MAXIMO_DETALLE_PROBLEMA}
          aria-invalid={!!errores.detalle}
          aria-describedby={errores.detalle ? 'problema-detalle-error' : undefined}
        />
        <p className={styles.ayuda}>
          {detalle.length}/{LARGO_MAXIMO_DETALLE_PROBLEMA}. Por ejemplo: 3 bolsas rotas, vino hierro del 8.
        </p>
        {errores.detalle && (
          <p id="problema-detalle-error" className={styles.errorCampo} role="alert">
            {errores.detalle}
          </p>
        )}
      </div>

      <div className={styles.grupo}>
        <span className={styles.label}>Fotos (opcional, hasta {MAX_FOTOS_EVIDENCIA})</span>
        <input
          ref={entradaCamaraRef}
          type="file"
          accept="image/*"
          capture="environment"
          hidden
          aria-label="Sacar foto"
          onChange={(e) => void agregarFotos(e)}
        />
        <input
          ref={entradaGaleriaRef}
          type="file"
          accept="image/*"
          multiple
          hidden
          aria-label="Elegir fotos de la galería"
          onChange={(e) => void agregarFotos(e)}
        />
        <div className={styles.botonesFoto}>
          <button
            type="button"
            className={styles.botonFoto}
            onClick={() => entradaCamaraRef.current?.click()}
            disabled={preparando || enviando || fotos.length >= MAX_FOTOS_EVIDENCIA}
          >
            Sacar foto
          </button>
          <button
            type="button"
            className={styles.botonFoto}
            onClick={() => entradaGaleriaRef.current?.click()}
            disabled={preparando || enviando || fotos.length >= MAX_FOTOS_EVIDENCIA}
          >
            Elegir de la galería
          </button>
        </div>
        {preparando && (
          <p className={styles.ayuda} role="status">
            Preparando la foto…
          </p>
        )}
        {errorFoto && (
          <p className={styles.errorCampo} role="alert">
            {errorFoto}
          </p>
        )}
        {errores.fotos && (
          <p className={styles.errorCampo} role="alert">
            {errores.fotos}
          </p>
        )}
        {fotos.length > 0 && (
          <ul className={styles.fotos} aria-label="Fotos elegidas">
            {fotos.map((foto, indice) => (
              <li key={foto.id} className={styles.foto}>
                <img src={foto.vistaPrevia} alt={`Foto ${indice + 1} para enviar`} className={styles.imagen} />
                <button type="button" className={styles.quitar} onClick={() => quitarFoto(foto.id)} disabled={enviando}>
                  Quitar foto {indice + 1}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <button type="submit" className={styles.boton} disabled={enviando || preparando} aria-busy={enviando}>
        {enviando ? 'Guardando…' : 'Registrar el problema'}
      </button>
      <button type="button" className={styles.cancelar} onClick={onCancelar} disabled={enviando}>
        Volver
      </button>
    </form>
  )
}
