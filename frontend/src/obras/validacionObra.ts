// Reglas de validación del formulario de alta de obra.
// OJO: son las que asumimos desde el frontend. El contrato no declara longitudes máximas ni
// otras reglas; si el squad de Usuarios define más, se ajustan acá. El backend valida igual
// (y puede responder 400), esto solo evita ir al servidor con datos obviamente incompletos.

export interface DatosObraForm {
  nombre: string
  ubicacion: string
  /** Formato que da <input type="date">: AAAA-MM-DD, o '' si está vacío. */
  fechaInicio: string
  capatazId: string
  etapas: string[]
}

export interface ErroresObra {
  nombre?: string
  ubicacion?: string
  fechaInicio?: string
  capatazId?: string
  /** Error general de la lista de etapas (ej. "agregá al menos una"). */
  etapas?: string
  /** Error por posición: etapasItems[2] es el error de la tercera etapa. */
  etapasItems?: Record<number, string>
}

const RE_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const RE_FECHA = /^\d{4}-\d{2}-\d{2}$/

export function validarObra(datos: DatosObraForm): ErroresObra {
  const errores: ErroresObra = {}

  if (!datos.nombre.trim()) {
    errores.nombre = 'El nombre es obligatorio.'
  }
  if (!datos.ubicacion.trim()) {
    errores.ubicacion = 'La ubicación es obligatoria.'
  }

  if (!datos.fechaInicio) {
    errores.fechaInicio = 'La fecha de inicio es obligatoria.'
  } else if (!RE_FECHA.test(datos.fechaInicio) || Number.isNaN(Date.parse(datos.fechaInicio))) {
    errores.fechaInicio = 'Ingresá una fecha válida.'
  }

  const capatazId = datos.capatazId.trim()
  if (!capatazId) {
    errores.capatazId = 'El capataz es obligatorio.'
  } else if (!RE_GUID.test(capatazId)) {
    errores.capatazId = 'El identificador del capataz debe ser un GUID válido.'
  }

  if (datos.etapas.length === 0) {
    errores.etapas = 'Agregá al menos una etapa.'
  } else {
    const items: Record<number, string> = {}
    const vistas = new Set<string>()
    datos.etapas.forEach((etapa, i) => {
      const nombre = etapa.trim()
      if (!nombre) {
        items[i] = 'El nombre de la etapa es obligatorio.'
      } else if (vistas.has(nombre.toLowerCase())) {
        items[i] = 'Esta etapa está repetida.'
      } else {
        vistas.add(nombre.toLowerCase())
      }
    })
    if (Object.keys(items).length > 0) {
      errores.etapasItems = items
    }
  }

  return errores
}

export function hayErrores(errores: ErroresObra): boolean {
  return Object.keys(errores).length > 0
}

/** Convierte AAAA-MM-DD en el date-time que pide el contrato (medianoche UTC). */
export function fechaAIso(fecha: string): string {
  return `${fecha}T00:00:00.000Z`
}
