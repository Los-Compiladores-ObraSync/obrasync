import { useEffect, useRef, useState } from 'react'
import { BotonVerMas } from '../componentes/BotonVerMas'
import styles from '../pages/NuevaRequisicionPage.module.css'
import { ESPERA_BUSQUEDA_MS } from './configuracionBusqueda'
import { obtenerInsumosCatalogo } from './requisicionesService'
import type { InsumoCatalogo } from './requisicionesTipos'

/** Lo que devolvió el servidor para un texto de búsqueda (#57, #172). */
interface ResultadoBusqueda {
  termino: string
  lista: InsumoCatalogo[]
  fallo: boolean
  /** Total que informa el servicio para esa búsqueda. Sin definir si no lo envía. */
  total?: number
  /** Última página traída (1 = la primera). */
  pagina: number
}

/** Agrega una página nueva a lo ya mostrado, sin repetir insumos. */
function sumarInsumos(actuales: InsumoCatalogo[], nuevos: InsumoCatalogo[]): InsumoCatalogo[] {
  const ids = new Set(actuales.map((ins) => ins.articuloId))
  return [...actuales, ...nuevos.filter((ins) => !ids.has(ins.articuloId))]
}

/** Cómo se muestra un insumo. Si el "código" es el propio id (pasa al editar o repetir un pedido,
 *  donde el servidor no manda el código), se muestra solo la descripción. */
function textoInsumo(ins: InsumoCatalogo): string {
  return ins.codigo === ins.articuloId ? ins.descripcion : `${ins.codigo} — ${ins.descripcion}`
}

interface Props {
  /** Número de la fila (1, 2, 3…): para el título y para los textos de accesibilidad. */
  numero: number
  /** Id del campo, para enlazarlo con su título. */
  id: string
  /** El insumo ya elegido en esta fila, si hay. */
  seleccionado?: InsumoCatalogo
  /** Insumos que ya están elegidos en otras filas: no se pueden repetir. */
  ocupados: ReadonlySet<string>
  /** Mensaje de error de la fila (por ejemplo, "Seleccioná un insumo del catálogo."). */
  error?: string
  /** Se tocó un resultado de la búsqueda. */
  onElegir: (insumo: InsumoCatalogo) => void
  /** Se tocó "Cambiar": la fila vuelve a quedar sin insumo para buscar otro. */
  onCambiar: () => void
  /** Si es true, el cursor va al campo de búsqueda cuando aparece la fila (filas que se agregan para seguir cargando, #237). */
  enfocarAlAparecer?: boolean
}

/**
 * Elegir el insumo de una fila buscándolo por código o nombre (reemplaza al desplegable, que listaba
 * el catálogo entero y era inmanejable). Mientras no hay insumo elegido se ve el campo de búsqueda
 * con sus resultados; cuando se elige, se ve el insumo con un botón "Cambiar".
 * Cada fila tiene su propio buscador: por eso el estado de la búsqueda vive acá y no en la pantalla.
 */
export function BuscadorInsumo({ numero, id, seleccionado, ocupados, error, onElegir, onCambiar, enfocarAlAparecer }: Props) {
  const [busqueda, setBusqueda] = useState('')
  // Lo escrito, pero recién después de una pausa (#58): así no se pide en cada letra.
  const [terminoConEspera, setTerminoConEspera] = useState('')
  const [resultadoBusqueda, setResultadoBusqueda] = useState<ResultadoBusqueda | null>(null)
  const [cargandoMas, setCargandoMas] = useState(false)
  const [errorMas, setErrorMas] = useState(false)
  const campo = useRef<HTMLInputElement>(null)
  const teniaSeleccion = useRef(!!seleccionado)

  const termino = busqueda.trim()

  useEffect(() => {
    const temporizador = setTimeout(() => setTerminoConEspera(termino), ESPERA_BUSQUEDA_MS)
    return () => clearTimeout(temporizador)
  }, [termino])

  // Si sale una búsqueda nueva mientras la anterior no volvió, la respuesta vieja se descarta
  // (el flag "cancelado").
  useEffect(() => {
    if (terminoConEspera === '') return
    let cancelado = false

    obtenerInsumosCatalogo(terminoConEspera)
      .then((pagina) => {
        if (!cancelado) {
          setResultadoBusqueda({
            termino: terminoConEspera,
            lista: pagina.items,
            total: pagina.total,
            pagina: 1,
            fallo: false,
          })
        }
      })
      .catch(() => {
        if (!cancelado) {
          setResultadoBusqueda({ termino: terminoConEspera, lista: [], pagina: 1, fallo: true })
        }
      })

    return () => {
      cancelado = true
    }
  }, [terminoConEspera])

  // Fila nueva agregada con "+ Agregar otro insumo": el cursor queda listo para buscar el siguiente (#237).
  useEffect(() => {
    if (enfocarAlAparecer) campo.current?.focus()
  }, [enfocarAlAparecer])

  // Al tocar "Cambiar" la fila queda sin insumo: el cursor va directo al campo de búsqueda.
  useEffect(() => {
    if (!seleccionado && teniaSeleccion.current) {
      campo.current?.focus()
    }
    teniaSeleccion.current = !!seleccionado
  }, [seleccionado])

  // Se calculan a partir de lo que ya hay, sin guardarlos en otro estado.
  const resultadoVigente = resultadoBusqueda?.termino === termino ? resultadoBusqueda : null
  const buscando = termino !== '' && resultadoVigente === null
  const falloBusqueda = resultadoVigente?.fallo === true
  const resultados = resultadoVigente !== null && !resultadoVigente.fallo ? resultadoVigente.lista : []
  const sinResultados =
    resultadoVigente !== null && !resultadoVigente.fallo && resultadoVigente.lista.length === 0

  /** Elige un resultado: se vacía lo escrito para que, si después se toca "Cambiar", se busque de cero. */
  function elegir(ins: InsumoCatalogo) {
    setBusqueda('')
    onElegir(ins)
  }

  /** "Ver más" de los resultados de la búsqueda actual (#172). */
  async function verMas() {
    const vigente = resultadoBusqueda
    if (cargandoMas || !vigente || vigente.termino !== termino) return
    setCargandoMas(true)
    setErrorMas(false)
    try {
      const pagina = await obtenerInsumosCatalogo(vigente.termino, true, vigente.pagina + 1)
      // Si mientras tanto se buscó otra cosa, esta respuesta ya no corresponde.
      setResultadoBusqueda((actual) =>
        actual && actual.termino === vigente.termino
          ? {
              ...actual,
              lista: sumarInsumos(actual.lista, pagina.items),
              total: pagina.total,
              pagina: actual.pagina + 1,
            }
          : actual,
      )
    } catch {
      setErrorMas(true)
    } finally {
      setCargandoMas(false)
    }
  }

  const idError = `${id}-error`
  const titulo = `Insumo #${numero}`

  // Ya hay insumo elegido: se muestra y se ofrece cambiarlo.
  if (seleccionado) {
    return (
      <div className={styles.grupo}>
        <label htmlFor={id} className={styles.label}>
          {titulo}
        </label>
        <div className={styles.filaBuscador}>
          <input
            id={id}
            type="text"
            readOnly
            className={`${styles.input} ${styles.buscador}`}
            value={textoInsumo(seleccionado)}
          />
          <button
            type="button"
            className={styles.secundario}
            aria-label={`Cambiar el insumo ${numero}`}
            onClick={onCambiar}
          >
            Cambiar
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className={styles.grupo}>
      <label htmlFor={id} className={styles.label}>
        {titulo}
      </label>
      <div className={styles.filaBuscador}>
        <input
          ref={campo}
          id={id}
          type="search"
          className={`${error ? `${styles.input} ${styles.inputInvalido}` : styles.input} ${styles.buscador}`}
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          // Enter dentro de un formulario lo envía: acá solo se está buscando. Si ya hay resultados,
          // elige el primero que no esté agregado (#237), así se carga sin soltar el teclado.
          onKeyDown={(e) => {
            if (e.key !== 'Enter') return
            e.preventDefault()
            const primero = resultados.find((ins) => !ocupados.has(ins.articuloId))
            if (primero) elegir(primero)
          }}
          placeholder="Buscá por código o nombre (ej: cemento)"
          autoComplete="off"
          aria-invalid={!!error}
          aria-describedby={error ? idError : undefined}
        />
        {busqueda !== '' && (
          <button
            type="button"
            className={styles.secundario}
            aria-label={`Limpiar la búsqueda del insumo ${numero}`}
            onClick={() => {
              setBusqueda('')
              campo.current?.focus()
            }}
          >
            Limpiar
          </button>
        )}
      </div>
      {error && (
        <p id={idError} className={styles.errorCampo} role="alert">
          {error}
        </p>
      )}
      {buscando && (
        <p role="status" className={styles.ayudaBusqueda}>
          Buscando…
        </p>
      )}
      {falloBusqueda && (
        <p role="alert" className={styles.errorCampo}>
          No se pudo buscar insumos. Intentá de nuevo.
        </p>
      )}
      {sinResultados && (
        <p role="status" className={styles.ayudaBusqueda}>
          No encontramos insumos con ese nombre
        </p>
      )}
      {resultados.length > 0 && (
        <>
          <p role="status" className={styles.ayudaBusqueda}>
            {resultados.length === 1 ? '1 insumo encontrado.' : `${resultados.length} insumos encontrados.`}{' '}
            Tocá uno para elegirlo.
          </p>
          <ul className={styles.listaResultados}>
            {resultados.map((ins) => {
              const yaAgregado = ocupados.has(ins.articuloId)
              return (
                <li key={ins.articuloId}>
                  <button
                    type="button"
                    className={`${styles.secundario} ${styles.resultado}`}
                    disabled={yaAgregado}
                    onClick={() => elegir(ins)}
                  >
                    {textoInsumo(ins)}
                    {yaAgregado ? ' (ya agregado)' : ''}
                  </button>
                </li>
              )
            })}
          </ul>
        </>
      )}
      {resultadoVigente !== null && !resultadoVigente.fallo && (
        <BotonVerMas
          cargadas={resultadoVigente.lista.length}
          total={resultadoVigente.total}
          nombre="insumos"
          cargando={cargandoMas}
          error={errorMas}
          onVerMas={() => void verMas()}
        />
      )}
    </div>
  )
}
