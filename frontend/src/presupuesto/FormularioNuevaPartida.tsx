import { useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../api/ApiError'
import { parsearMonto } from './montoTexto'
import { crearPartida } from './presupuestoService'
import type { PartidaPresupuestaria } from './presupuestoTipos'
import styles from './FormularioNuevaPartida.module.css'

interface Props {
  obraId: string
  onCreada: (partida: PartidaPresupuestaria) => void
}

interface ErroresCampos {
  codigo?: string
  descripcion?: string
  etapa?: string
  monto?: string
}

function mensajeDeErrorCrear(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'Solo un Comprador o un Administrador puede cargar partidas.'
    if (err.status === 409) {
      return err.detail ?? 'Ya existe una partida con ese código o para esa etapa en la obra.'
    }
    if (err.status === 400) return err.detail ?? 'Datos de la partida inválidos.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

/**
 * Formulario para dar de alta una partida presupuestaria de la obra elegida (#161).
 * Solo se muestra a Comprador y Admin (lo decide la pantalla que lo usa; el backend lo vuelve a validar).
 */
export function FormularioNuevaPartida({ obraId, onCreada }: Props) {
  const [codigo, setCodigo] = useState('')
  const [descripcion, setDescripcion] = useState('')
  const [etapa, setEtapa] = useState('')
  const [monto, setMonto] = useState('')
  const [errores, setErrores] = useState<ErroresCampos>({})
  const [errorApi, setErrorApi] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Protección inmediata contra el doble clic (el estado tarda un render en reflejarse)
  const enVueloRef = useRef(false)

  function validar(): { errores: ErroresCampos; montoNumero: number | null } {
    const nuevos: ErroresCampos = {}
    if (codigo.trim() === '') nuevos.codigo = 'El código es obligatorio.'
    else if (codigo.trim().length > 50) nuevos.codigo = 'El código no puede superar los 50 caracteres.'
    if (descripcion.trim() === '') nuevos.descripcion = 'La descripción es obligatoria.'
    else if (descripcion.trim().length > 250) {
      nuevos.descripcion = 'La descripción no puede superar los 250 caracteres.'
    }
    if (etapa.trim().length > 200) nuevos.etapa = 'La etapa no puede superar los 200 caracteres.'
    const montoNumero = parsearMonto(monto)
    if (montoNumero === null) {
      nuevos.monto = 'Ingresá un monto en pesos, sin coma ni decimales, mayor o igual a cero (ej: 1500000 o 1.500.000).'
    }
    return { errores: nuevos, montoNumero }
  }

  async function handleSubmit(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault()
    if (enVueloRef.current) return

    const { errores: nuevos, montoNumero } = validar()
    setErrores(nuevos)
    setErrorApi(null)
    if (Object.keys(nuevos).length > 0 || montoNumero === null) return

    enVueloRef.current = true
    setEnviando(true)
    try {
      const creada = await crearPartida({
        obraId,
        etapa: etapa.trim() === '' ? null : etapa.trim(),
        codigoPartida: codigo.trim(),
        descripcion: descripcion.trim(),
        montoAsignado: montoNumero,
        moneda: 'ARS',
      })
      setCodigo('')
      setDescripcion('')
      setEtapa('')
      setMonto('')
      onCreada(creada)
    } catch (err) {
      setErrorApi(mensajeDeErrorCrear(err))
    } finally {
      enVueloRef.current = false
      setEnviando(false)
    }
  }

  return (
    <form className={styles.formulario} onSubmit={handleSubmit} noValidate aria-label="Cargar partida presupuestaria">
      <h2 className={styles.titulo}>Cargar una partida</h2>

      <div className={styles.campo}>
        <label htmlFor="partida-codigo">Código de la partida</label>
        <input
          id="partida-codigo"
          type="text"
          value={codigo}
          onChange={(e) => setCodigo(e.target.value)}
          aria-invalid={errores.codigo ? true : undefined}
          aria-describedby={errores.codigo ? 'partida-codigo-error' : undefined}
        />
        {errores.codigo && (
          <p id="partida-codigo-error" className={styles.errorCampo}>{errores.codigo}</p>
        )}
      </div>

      <div className={styles.campo}>
        <label htmlFor="partida-descripcion">Descripción</label>
        <input
          id="partida-descripcion"
          type="text"
          value={descripcion}
          onChange={(e) => setDescripcion(e.target.value)}
          aria-invalid={errores.descripcion ? true : undefined}
          aria-describedby={errores.descripcion ? 'partida-descripcion-error' : undefined}
        />
        {errores.descripcion && (
          <p id="partida-descripcion-error" className={styles.errorCampo}>{errores.descripcion}</p>
        )}
      </div>

      <div className={styles.campo}>
        <label htmlFor="partida-etapa">Etapa (opcional)</label>
        <input
          id="partida-etapa"
          type="text"
          value={etapa}
          onChange={(e) => setEtapa(e.target.value)}
          aria-invalid={errores.etapa ? true : undefined}
          aria-describedby="partida-etapa-ayuda"
        />
        <p id="partida-etapa-ayuda" className={styles.ayuda}>
          Si la dejás vacía, la partida es general de la obra.
        </p>
        {errores.etapa && <p className={styles.errorCampo}>{errores.etapa}</p>}
      </div>

      <div className={styles.campo}>
        <label htmlFor="partida-monto">Monto asignado (ARS)</label>
        <input
          id="partida-monto"
          type="text"
          inputMode="numeric"
          value={monto}
          onChange={(e) => { if (e.target.value.includes(',')) return; setMonto(e.target.value) }}
          aria-invalid={errores.monto ? true : undefined}
          aria-describedby={errores.monto ? 'partida-monto-error' : undefined}
        />
        {errores.monto && (
          <p id="partida-monto-error" className={styles.errorCampo}>{errores.monto}</p>
        )}
      </div>

      {errorApi && (
        <p role="alert" className={styles.errorApi}>{errorApi}</p>
      )}

      <button type="submit" className={styles.boton} disabled={enviando}>
        {enviando ? 'Guardando…' : 'Guardar partida'}
      </button>
    </form>
  )
}
