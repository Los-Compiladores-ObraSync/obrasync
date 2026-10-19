import { describe, expect, it } from 'vitest'
import {
  hayErrores,
  mapearEstadoRequisicion,
  validarRequisicion,
  type RequisicionForm,
} from './validacionRequisicion'

describe('validarRequisicion', () => {
  const formValido: RequisicionForm = {
    obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
    etapa: 'Estructura',
    observaciones: 'Para hormigonado de losa',
    items: [
      {
        articuloId: 'c1c2c3c4-0000-0000-0000-000000000001',
        cantidad: '80',
      },
    ],
  }

  it('no devuelve errores cuando el formulario es válido', () => {
    const errores = validarRequisicion(formValido)
    expect(hayErrores(errores)).toBe(false)
    expect(errores.obraId).toBeUndefined()
    expect(errores.etapa).toBeUndefined()
    expect(errores.items).toBeUndefined()
    expect(errores.itemsGeneral).toBeUndefined()
  })

  it('acepta cantidades con decimales mayores a cero', () => {
    const formDecimal: RequisicionForm = {
      ...formValido,
      items: [
        {
          articuloId: 'c1c2c3c4-0000-0000-0000-000000000001',
          cantidad: '12.5',
        },
      ],
    }
    const errores = validarRequisicion(formDecimal)
    expect(hayErrores(errores)).toBe(false)
  })

  it('falla si falta seleccionar la obra', () => {
    const errores = validarRequisicion({ ...formValido, obraId: '' })
    expect(hayErrores(errores)).toBe(true)
    expect(errores.obraId).toBe('Tenés que seleccionar una obra.')
  })

  it('falla si falta seleccionar la etapa', () => {
    const errores = validarRequisicion({ ...formValido, etapa: '' })
    expect(hayErrores(errores)).toBe(true)
    expect(errores.etapa).toBe('Tenés que seleccionar una etapa de la obra.')
  })

  it('falla si no hay ítems', () => {
    const errores = validarRequisicion({ ...formValido, items: [] })
    expect(hayErrores(errores)).toBe(true)
    expect(errores.itemsGeneral).toBe('Tenés que agregar al menos un ítem a la requisición.')
  })

  it('falla si un ítem no tiene insumo seleccionado', () => {
    const errores = validarRequisicion({
      ...formValido,
      items: [{ articuloId: '', cantidad: '10' }],
    })
    expect(hayErrores(errores)).toBe(true)
    expect(errores.items?.[0]?.articuloId).toBe('Seleccioná un insumo del catálogo.')
  })

  it('falla si la cantidad está vacía, es cero o es negativa', () => {
    const errorVacio = validarRequisicion({
      ...formValido,
      items: [{ articuloId: 'art-1', cantidad: '' }],
    })
    expect(errorVacio.items?.[0]?.cantidad).toBe('Ingresá una cantidad.')

    const errorCero = validarRequisicion({
      ...formValido,
      items: [{ articuloId: 'art-1', cantidad: '0' }],
    })
    expect(errorCero.items?.[0]?.cantidad).toBe('La cantidad debe ser mayor a 0.')

    const errorNegativo = validarRequisicion({
      ...formValido,
      items: [{ articuloId: 'art-1', cantidad: '-5' }],
    })
    expect(errorNegativo.items?.[0]?.cantidad).toBe('La cantidad debe ser mayor a 0.')
  })

  it('falla si se repite el mismo insumo en la lista', () => {
    const errores = validarRequisicion({
      ...formValido,
      items: [
        { articuloId: 'art-1', cantidad: '10' },
        { articuloId: 'art-1', cantidad: '20' },
      ],
    })
    expect(hayErrores(errores)).toBe(true)
    expect(errores.items?.[1]?.articuloId).toBe('Este insumo ya fue agregado en la lista.')
  })
})

describe('mapearEstadoRequisicion', () => {
  it('mapea Pendiente a "Aprobación Pendiente"', () => {
    expect(mapearEstadoRequisicion('Pendiente')).toBe('Aprobación Pendiente')
  })

  it('mapea Aprobada a "Aprobada"', () => {
    expect(mapearEstadoRequisicion('Aprobada')).toBe('Aprobada')
  })

  it('mapea Rechazada a "Rechazada"', () => {
    expect(mapearEstadoRequisicion('Rechazada')).toBe('Rechazada')
  })

  it('devuelve el estado original si no es ninguno de los conocidos', () => {
    expect(mapearEstadoRequisicion('Desconocido')).toBe('Desconocido')
  })
})
