import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { Link } from 'react-router'
import { cargarInsumos, type ResultadoCarga } from '../catalogo/catalogoService'
import {
  MAX_BYTES,
  MAX_FILAS,
  PLANTILLA_CSV,
  validarCsv,
  type ResultadoValidacion,
} from '../catalogo/csvInsumos'
import styles from './CargaMasivaInsumosPage.module.css'

type Fase = 'elegir' | 'listo' | 'cargando' | 'terminado'

const ENLACE_PLANTILLA = `data:text/csv;charset=utf-8,${encodeURIComponent(`\uFEFF${PLANTILLA_CSV}`)}`

function leerArchivo(archivo: File, codificacion?: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const lector = new FileReader()
    lector.onload = () => resolve(String(lector.result ?? ''))
    lector.onerror = () => reject(lector.error)
    lector.readAsText(archivo, codificacion)
  })
}

/** Lee el CSV como UTF-8; si aparecen caracteres rotos, el archivo venía de Excel en "ANSI" y se relee así. */
async function leerCsv(archivo: File): Promise<string> {
  const texto = await leerArchivo(archivo)
  return texto.includes('�') ? leerArchivo(archivo, 'windows-1252') : texto
}

function plural(cantidad: number, singular: string, pluralTexto: string): string {
  return `${cantidad} ${cantidad === 1 ? singular : pluralTexto}`
}

/**
 * Carga masiva de insumos del catálogo desde un CSV (#215, decisión de la #116).
 * Se lee en el navegador y se envía fila por fila a POST /api/catalogo/insumos. Roles: Comprador y Admin.
 */
export function CargaMasivaInsumosPage() {
  const [fase, setFase] = useState<Fase>('elegir')
  const [nombreArchivo, setNombreArchivo] = useState('')
  const [validacion, setValidacion] = useState<ResultadoValidacion | null>(null)
  const [errorLectura, setErrorLectura] = useState<string | null>(null)
  const [progreso, setProgreso] = useState({ hechas: 0, total: 0 })
  const [resultado, setResultado] = useState<ResultadoCarga | null>(null)
  // Cambiar la clave vuelve a armar el campo de archivo (para poder elegir el mismo archivo otra vez).
  const [claveCampo, setClaveCampo] = useState(0)
  const montada = useRef(true)

  useEffect(() => {
    montada.current = true
    return () => {
      montada.current = false
    }
  }, [])

  async function alElegirArchivo(evento: ChangeEvent<HTMLInputElement>) {
    const archivo = evento.target.files?.[0]
    if (!archivo) return
    setErrorLectura(null)
    setValidacion(null)
    setFase('elegir')
    setNombreArchivo(archivo.name)
    if (archivo.size > MAX_BYTES) {
      setErrorLectura('El archivo es demasiado grande. Dividilo en archivos más chicos.')
      return
    }
    try {
      const texto = await leerCsv(archivo)
      if (!montada.current) return
      const revisado = validarCsv(texto)
      setValidacion(revisado)
      setFase(revisado.errorGeneral === null && revisado.insumos.length > 0 ? 'listo' : 'elegir')
    } catch {
      if (montada.current) setErrorLectura('No se pudo leer el archivo. Probá de nuevo.')
    }
  }

  async function cargar() {
    if (!validacion || validacion.insumos.length === 0) return
    setFase('cargando')
    setProgreso({ hechas: 0, total: validacion.insumos.length })
    const final = await cargarInsumos(validacion.insumos, {
      alProgreso: (hechas, total) => {
        if (montada.current) setProgreso({ hechas, total })
      },
      estaCancelado: () => !montada.current,
    })
    if (!montada.current) return
    setResultado(final)
    setFase('terminado')
  }

  function empezarDeNuevo() {
    setFase('elegir')
    setNombreArchivo('')
    setValidacion(null)
    setErrorLectura(null)
    setResultado(null)
    setClaveCampo((clave) => clave + 1)
  }

  const cantidadListos = validacion?.insumos.length ?? 0
  const erroresFormato = validacion?.errores ?? []

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1 className={styles.titulo}>Cargar insumos desde un CSV</h1>
      <p className={styles.intro}>
        Subí un archivo CSV y los insumos se agregan al catálogo. Si usás Excel, guardalo como «CSV».
      </p>

      {fase !== 'terminado' && (
        <>
          <ul className="lista-acciones">
            <li>
              <a href={ENLACE_PLANTILLA} download="plantilla-insumos.csv" className="accion-secundaria">
                Descargar la plantilla
              </a>
            </li>
          </ul>
          <p className={styles.ayuda}>
            Columnas obligatorias: codigo, descripcion y unidadMedida. Opcionales: moneda, precioReferencia
            y activo (si o no). Hasta {MAX_FILAS} filas por archivo.
          </p>

          <div className={styles.campo}>
            <label htmlFor="archivo-insumos">Archivo CSV</label>
            <input
              key={claveCampo}
              id="archivo-insumos"
              type="file"
              accept=".csv,text/csv"
              onChange={alElegirArchivo}
              disabled={fase === 'cargando'}
            />
          </div>
        </>
      )}

      {errorLectura && (
        <p role="alert" className={styles.error}>
          {errorLectura}
        </p>
      )}

      {validacion?.errorGeneral && (
        <p role="alert" className={styles.error}>
          {validacion.errorGeneral}
        </p>
      )}

      {validacion && validacion.errorGeneral === null && fase !== 'terminado' && (
        <div className={styles.resumen}>
          <p className={styles.resumenTitulo}>
            {nombreArchivo}: {plural(cantidadListos, 'insumo listo', 'insumos listos')} para cargar
          </p>
          {erroresFormato.length > 0 && (
            <>
              <p className={styles.aviso}>
                {plural(erroresFormato.length, 'fila tiene', 'filas tienen')} errores de formato y no se va a
                enviar:
              </p>
              <ul className={styles.lista}>
                {erroresFormato.map((error) => (
                  <li key={error.fila}>
                    Fila {error.fila}: {error.motivo}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}

      {fase === 'listo' && (
        <button type="button" className="accion-grande" onClick={cargar}>
          Cargar {plural(cantidadListos, 'insumo', 'insumos')}
        </button>
      )}

      {fase === 'cargando' && (
        <p role="status" className={styles.progreso}>
          Cargando {progreso.hechas} de {progreso.total}…
        </p>
      )}

      {fase === 'terminado' && resultado && (
        <div className={styles.resumen} role="status">
          <p className={styles.resumenTitulo}>
            Se cargaron {resultado.cargados} de {cantidadListos} insumos.
          </p>
          {resultado.motivoCorte && (
            <p role="alert" className={styles.error}>
              {resultado.motivoCorte} No se enviaron {plural(resultado.sinEnviar, 'fila', 'filas')}.
            </p>
          )}
          {resultado.fallidas.length > 0 && (
            <>
              <p className={styles.aviso}>
                {plural(resultado.fallidas.length, 'fila no se cargó', 'filas no se cargaron')}:
              </p>
              <ul className={styles.lista}>
                {resultado.fallidas.map((fallida) => (
                  <li key={fallida.fila}>
                    Fila {fallida.fila} ({fallida.codigo}): {fallida.motivo}
                  </li>
                ))}
              </ul>
            </>
          )}
          {erroresFormato.length > 0 && (
            <p className={styles.aviso}>
              Además, {plural(erroresFormato.length, 'fila quedó', 'filas quedaron')} sin enviar por errores de
              formato.
            </p>
          )}
          <button type="button" className="accion-grande" onClick={empezarDeNuevo}>
            Cargar otro archivo
          </button>
        </div>
      )}
    </section>
  )
}
