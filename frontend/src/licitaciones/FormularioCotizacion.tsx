import { type FormEvent, useState } from 'react'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from '../pages/CotizarLicitacionPage.module.css'
import { formatoPrecio } from './cotizacionesCalculos'
import { PlazoEntregaOpciones } from './PlazoEntregaOpciones'
import type { PliegoItemDto } from './licitacionesTipos'
import type { CotizarConMagicLinkRequestDto } from './magicLinkTipos'
import {
  hayErroresCotizacion,
  parsearNumeroDecimal,
  validarCotizacion,
  type OtraPresentacionFormulario,
} from './validacionCotizacion'

interface Props {
  items: PliegoItemDto[]
  /** Mientras se envía, todos los campos quedan bloqueados. */
  enviando: boolean
  /** El enlace ya no sirve (venció o se usó): el formulario queda de solo lectura. */
  bloqueado: boolean
  textoBoton: string
  /** Se llama solo con datos que ya pasaron la validación. */
  onEnviar: (valores: CotizarConMagicLinkRequestDto) => void
}

/**
 * Formulario para cotizar un pliego: precio por ítem (con la opción de ofrecer otra presentación, #229),
 * flete y plazo de entrega. No sabe quién cotiza ni cómo se envía: eso lo resuelve quien lo usa.
 * Las reglas de validación son las de siempre (validacionCotizacion.ts).
 */
export function FormularioCotizacion({ items, enviando, bloqueado, textoBoton, onEnviar }: Props) {
  const [precios, setPrecios] = useState<Record<string, string>>({})
  const [flete, setFlete] = useState('')
  const [plazoEntregaDias, setPlazoEntregaDias] = useState('')
  // Ítems donde se activó "Ofrecer otra presentación". Sin entrada = se cotiza lo pedido.
  const [otras, setOtras] = useState<Record<string, OtraPresentacionFormulario | undefined>>({})
  const [tocados, setTocados] = useState<Record<string, boolean>>({})
  const [intentoEnviar, setIntentoEnviar] = useState(false)

  const deshabilitado = enviando || bloqueado
  const errores = validarCotizacion(
    { flete, plazoEntregaDias, preciosPorArticulo: precios, otrasPresentacionesPorArticulo: otras },
    items.map((it) => it.articuloId),
  )
  // Un error se muestra recién cuando la persona salió del campo o intentó enviar.
  const visible = (clave: string) => intentoEnviar || Boolean(tocados[clave])
  const tocar = (clave: string) => setTocados((prev) => ({ ...prev, [clave]: true }))

  function activarOtraPresentacion(articuloId: string, activa: boolean) {
    setOtras((prev) => {
      const siguiente = { ...prev }
      if (activa) siguiente[articuloId] = { presentacion: '', cantidad: '' }
      else delete siguiente[articuloId]
      return siguiente
    })
  }

  function cambiarOtra(articuloId: string, campo: 'presentacion' | 'cantidad', valor: string) {
    setOtras((prev) => ({
      ...prev,
      [articuloId]: {
        presentacion: prev[articuloId]?.presentacion ?? '',
        cantidad: prev[articuloId]?.cantidad ?? '',
        [campo]: valor,
      },
    }))
  }

  function enviar(e: FormEvent) {
    e.preventDefault()
    if (deshabilitado) return
    setIntentoEnviar(true)
    if (hayErroresCotizacion(errores)) return

    onEnviar({
      flete: parsearNumeroDecimal(flete) ?? 0,
      plazoEntregaDias: Number(plazoEntregaDias.trim()),
      items: items.map((it) => {
        const otra = otras[it.articuloId]
        return {
          articuloId: it.articuloId,
          precioUnitario: parsearNumeroDecimal(precios[it.articuloId] ?? '') ?? 0,
          // Solo se mandan los campos de la otra presentación si se activó en este ítem.
          ...(otra
            ? {
                presentacionOfrecida: otra.presentacion.trim(),
                cantidadOfrecida: parsearNumeroDecimal(otra.cantidad) ?? undefined,
              }
            : {}),
        }
      }),
    })
  }

  const claseInput = (conError: boolean) => (conError ? `${styles.input} ${styles.inputInvalido}` : styles.input)

  return (
    <form onSubmit={enviar} className={styles.formulario} noValidate>
      <fieldset className={styles.itemsFieldset}>
        <legend className={styles.itemsLegend}>Materiales requeridos</legend>
        {items.map((it) => {
          const id = it.articuloId
          const otra = otras[id]
          const errorPrecio = visible(id) ? errores.preciosPorArticulo?.[id] : undefined
          const erroresOtra = errores.otrasPresentacionesPorArticulo?.[id]
          const errorPresentacion = visible(`presentacion-${id}`) ? erroresOtra?.presentacion : undefined
          const errorCantidad = visible(`cantidad-${id}`) ? erroresOtra?.cantidad : undefined
          const unidadTexto = cantidadConUnidad(it.cantidad, it.unidadMedida)
          const cantidadOfrecida = otra ? parsearNumeroDecimal(otra.cantidad) : null
          const precioOfrecido = parsearNumeroDecimal(precios[id] ?? '')
          // Mismo cálculo que el contrato (#190): cantidad ofrecida × precio por unidad ofrecida.
          const totalOfrecido =
            cantidadOfrecida !== null && cantidadOfrecida > 0 && precioOfrecido !== null && precioOfrecido > 0
              ? Math.round(cantidadOfrecida * precioOfrecido * 100) / 100
              : null

          return (
            <div key={id} className={styles.filaItem}>
              <div className={styles.infoItem}>
                <strong className={styles.descripcionItem}>{it.descripcion}</strong>
                <span className={styles.cantidadItem}>Cantidad: {unidadTexto}</span>
              </div>

              <button
                type="button"
                className={styles.botonOtraPresentacion}
                aria-expanded={Boolean(otra)}
                onClick={() => activarOtraPresentacion(id, !otra)}
                disabled={deshabilitado}
              >
                {otra ? 'Cancelar' : 'Ofrecer otra presentación'}
              </button>

              {otra && (
                <div className={styles.bloqueOtraPresentacion}>
                  <div className={styles.grupo}>
                    <label htmlFor={`presentacion-${id}`} className={styles.label}>Presentación</label>
                    <p className={styles.ayudaCampo}>
                      Escribí qué ofrecés en lugar de lo pedido. Por ejemplo: bolsa de 25 kg.
                    </p>
                    <input
                      id={`presentacion-${id}`}
                      type="text"
                      className={claseInput(Boolean(errorPresentacion))}
                      value={otra.presentacion}
                      onChange={(e) => cambiarOtra(id, 'presentacion', e.target.value)}
                      onBlur={() => tocar(`presentacion-${id}`)}
                      placeholder="Ej: bolsa de 25 kg"
                      disabled={deshabilitado}
                    />
                    {errorPresentacion && <p className={styles.errorCampo} role="alert">{errorPresentacion}</p>}
                  </div>
                  <div className={styles.grupo}>
                    <label htmlFor={`cantidad-${id}`} className={styles.label}>Cantidad de unidades</label>
                    <input
                      id={`cantidad-${id}`}
                      type="text"
                      inputMode="decimal"
                      className={claseInput(Boolean(errorCantidad))}
                      value={otra.cantidad}
                      onChange={(e) => cambiarOtra(id, 'cantidad', e.target.value)}
                      onBlur={() => tocar(`cantidad-${id}`)}
                      placeholder="Ej: 2"
                      disabled={deshabilitado}
                    />
                    {errorCantidad && <p className={styles.errorCampo} role="alert">{errorCantidad}</p>}
                  </div>
                </div>
              )}

              <div className={styles.grupo}>
                <label htmlFor={`precio-${id}`} className={styles.label}>Precio unitario ($)</label>
                <input
                  id={`precio-${id}`}
                  type="text"
                  inputMode="decimal"
                  className={claseInput(Boolean(errorPrecio))}
                  value={precios[id] ?? ''}
                  onChange={(e) => setPrecios((prev) => ({ ...prev, [id]: e.target.value }))}
                  onBlur={() => tocar(id)}
                  placeholder="0,00"
                  disabled={deshabilitado}
                />
                {errorPrecio && <p className={styles.errorCampo} role="alert">{errorPrecio}</p>}
              </div>

              {otra && totalOfrecido !== null && (
                <p className={styles.resumenOferta} role="status">
                  Total que ofrecés: <strong>{formatoPrecio(totalOfrecido)}</strong> · Pedido: {unidadTexto}
                </p>
              )}
            </div>
          )
        })}
      </fieldset>

      <div className={styles.grupo}>
        <label htmlFor="flete" className={styles.label}>Costo de flete ($)</label>
        <input
          id="flete"
          type="text"
          inputMode="decimal"
          className={claseInput(visible('flete') && Boolean(errores.flete))}
          value={flete}
          onChange={(e) => setFlete(e.target.value)}
          onBlur={() => tocar('flete')}
          placeholder="0,00"
          disabled={deshabilitado}
        />
        {visible('flete') && errores.flete && <p className={styles.errorCampo} role="alert">{errores.flete}</p>}
      </div>

      <div className={styles.grupo}>
        <label htmlFor="plazoEntregaDias" className={styles.label}>Plazo de entrega (en días)</label>
        <p className={styles.ayudaCampo}>Elegí un plazo frecuente o escribí otro: solo el número de días corridos. Por ejemplo, 5 significa cinco días.</p>
        <PlazoEntregaOpciones valor={plazoEntregaDias} deshabilitado={deshabilitado} onElegir={(dias) => setPlazoEntregaDias(String(dias))} />
        <input
          id="plazoEntregaDias"
          type="text"
          inputMode="numeric"
          className={claseInput(visible('plazoEntregaDias') && Boolean(errores.plazoEntregaDias))}
          value={plazoEntregaDias}
          onChange={(e) => setPlazoEntregaDias(e.target.value)}
          onBlur={() => tocar('plazoEntregaDias')}
          placeholder="Ej: 7"
          disabled={deshabilitado}
        />
        {visible('plazoEntregaDias') && errores.plazoEntregaDias && (
          <p className={styles.errorCampo} role="alert">{errores.plazoEntregaDias}</p>
        )}
      </div>

      <button type="submit" className={styles.botonEnviar} disabled={deshabilitado}>
        {enviando ? 'Enviando…' : textoBoton}
      </button>
    </form>
  )
}
