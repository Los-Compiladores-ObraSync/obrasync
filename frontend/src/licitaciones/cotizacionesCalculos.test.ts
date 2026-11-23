import { describe, expect, it } from 'vitest'
import type { CotizacionResponseDto } from './licitacionesTipos'
import {
  calcularPrecioTotalCotizacion,
  calcularTotalConFlete,
  desglosarCotizacion,
  estrellasDePuntaje,
  formatoPrecio,
  marcasDeComparacion,
  ordenarCotizaciones,
  subtotalDeItem,
  tieneCoberturaParcial,
  tienePresentacionDistinta,
} from './cotizacionesCalculos'

describe('cotizacionesCalculos', () => {
  const licitacion = {
    licitacionId: 'lic-1',
    requisicionId: 'req-1',
    obraId: 'obra-1',
    estado: 'Abierta',
    fechaCreacion: '2026-10-01T00:00:00Z',
    fechaLimiteCotizacion: '2026-10-20T00:00:00Z',
    items: [
      {
        articuloId: 'art-cemento',
        descripcion: 'Cemento 25kg',
        cantidad: 80,
        unidadMedida: 'BOLSA',
      },
      {
        articuloId: 'art-arena',
        descripcion: 'Arena fina',
        cantidad: 15,
        unidadMedida: 'M3',
      },
    ],
  }

  it('calcula el precio total como la suma de precioUnitario × cantidad de cada ítem de la licitación', () => {
    const cotizacion = {
      items: [
        { articuloId: 'art-cemento', precioUnitario: 1000 },
        { articuloId: 'art-arena', precioUnitario: 2000 },
      ],
    }

    // 80 * 1000 + 15 * 2000 = 80000 + 30000 = 110000
    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(110000)
  })

  it('asume cantidad 0 si un ítem cotizado no existe en el pliego de la licitación', () => {
    const cotizacion = {
      items: [
        { articuloId: 'art-desconocido', precioUnitario: 5000 },
        { articuloId: 'art-cemento', precioUnitario: 1000 },
      ],
    }

    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(80000)
  })

  it('devuelve 0 si la cotización no tiene ítems', () => {
    const cotizacion = { items: [] }
    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(0)
  })

  it('formatea el precio con signo $ y separadores locales sin código de moneda ARS', () => {
    expect(formatoPrecio(850000)).toMatch(/^\$\s*850\.000$/)
    expect(formatoPrecio(0)).toMatch(/^\$\s*0$/)
  })

  describe('detalle para Compras', () => {
    const barata = {
      cotizacionId: 'c-1',
      flete: 15000,
      plazoEntregaDias: 5,
      items: [
        { articuloId: 'art-cemento', precioUnitario: 8000 },
        { articuloId: 'art-arena', precioUnitario: 14000 },
      ],
    }
    const cara = {
      cotizacionId: 'c-2',
      flete: 25000,
      plazoEntregaDias: 10,
      items: [
        { articuloId: 'art-cemento', precioUnitario: 8500 },
        { articuloId: 'art-arena', precioUnitario: 16000 },
      ],
    }

    it('el total con flete suma el flete al precio total (y sin flete suma 0)', () => {
      expect(calcularTotalConFlete(barata, licitacion)).toBe(850000 + 15000)
      expect(calcularTotalConFlete({ items: barata.items }, licitacion)).toBe(850000)
    })

    it('el desglose trae una línea por material del pliego con su subtotal', () => {
      const lineas = desglosarCotizacion(barata, licitacion)

      expect(lineas).toEqual([
        {
          articuloId: 'art-cemento',
          descripcion: 'Cemento 25kg',
          cantidad: 80,
          unidadMedida: 'BOLSA',
          precioUnitario: 8000,
          subtotal: 640000,
        },
        {
          articuloId: 'art-arena',
          descripcion: 'Arena fina',
          cantidad: 15,
          unidadMedida: 'M3',
          precioUnitario: 14000,
          subtotal: 210000,
        },
      ])
    })

    it('un material que el proveedor no cotizó queda sin precio y con subtotal 0', () => {
      const lineas = desglosarCotizacion({ items: [barata.items[0]] }, licitacion)

      expect(lineas[1].precioUnitario).toBeUndefined()
      expect(lineas[1].subtotal).toBe(0)
    })

    it('marca la mejor oferta en precio, flete y plazo', () => {
      const marcas = marcasDeComparacion([barata, cara], licitacion)

      expect(marcas.get('c-1')).toEqual(['Más barata', 'Flete más bajo', 'Entrega más rápida'])
      expect(marcas.get('c-2')).toEqual([])
    })

    it('reparte las marcas cuando cada oferta gana en algo distinto', () => {
      const rapida = { ...cara, plazoEntregaDias: 2 }
      const marcas = marcasDeComparacion([barata, rapida], licitacion)

      expect(marcas.get('c-1')).toEqual(['Más barata', 'Flete más bajo'])
      expect(marcas.get('c-2')).toEqual(['Entrega más rápida'])
    })

    it('"Más barata" cuenta el flete: la de precio menor puede perder si su flete es mucho más caro', () => {
      const materialesBaratosFleteCaro = { ...barata, flete: 200000 }   // 850.000 + 200.000 = 1.050.000
      const marcas = marcasDeComparacion([materialesBaratosFleteCaro, cara], licitacion) // cara: 920.000 + 25.000

      expect(marcas.get('c-1')).not.toContain('Más barata')
      expect(marcas.get('c-2')).toContain('Más barata')
    })

    it('con una sola cotización no marca nada', () => {
      expect(marcasDeComparacion([barata], licitacion).get('c-1')).toEqual([])
    })

    it('si hay empate en un criterio no marca ese criterio, y si el empate es en lo mejor, marca a todas', () => {
      const igualFlete = { ...cara, flete: 15000 }
      const empatadas = marcasDeComparacion([barata, igualFlete], licitacion)
      expect(empatadas.get('c-1')).not.toContain('Flete más bajo')
      expect(empatadas.get('c-2')).not.toContain('Flete más bajo')

      const tres = marcasDeComparacion(
        [barata, { ...barata, cotizacionId: 'c-3' }, cara],
        licitacion,
      )
      expect(tres.get('c-1')).toContain('Más barata')
      expect(tres.get('c-3')).toContain('Más barata')
      expect(tres.get('c-2')).toEqual([])
    })

    it('un flete o un plazo sin informar cuentan como 0 e inmediato', () => {
      const sinDatos = { cotizacionId: 'c-4', items: barata.items }
      const marcas = marcasDeComparacion([sinDatos, cara], licitacion)

      expect(marcas.get('c-4')).toEqual(['Más barata', 'Flete más bajo', 'Entrega más rápida'])
    })
  })
})

describe('otra presentación en la oferta (#229)', () => {
  const licitacion = {
    licitacionId: 'lic-1',
    requisicionId: 'req-1',
    obraId: 'obra-1',
    estado: 'Abierta',
    fechaCreacion: '2026-10-01T00:00:00Z',
    fechaLimiteCotizacion: '2026-10-20T00:00:00Z',
    items: [
      { articuloId: 'art-cemento', descripcion: 'Cemento 25kg', cantidad: 80, unidadMedida: 'BOLSA' },
      { articuloId: 'art-arena', descripcion: 'Arena fina', cantidad: 15, unidadMedida: 'M3' },
    ],
  }
  const conOtraPresentacion = {
    items: [
      { articuloId: 'art-cemento', precioUnitario: 1000, presentacionOfrecida: ' bolsa 25 kg ', cantidadOfrecida: 2, coberturaParcial: true },
      { articuloId: 'art-arena', precioUnitario: 14000 },
    ],
  }

  it('el subtotal usa la cantidad ofrecida; sin ella, la del pliego; y si el servicio lo mandó, ese', () => {
    expect(subtotalDeItem({ precioUnitario: 1000, cantidadOfrecida: 2 }, 80)).toBe(2000)
    expect(subtotalDeItem({ precioUnitario: 1000 }, 80)).toBe(80000)
    expect(subtotalDeItem({ precioUnitario: 1000, cantidadOfrecida: null }, 80)).toBe(80000)
    expect(subtotalDeItem({ precioUnitario: 1000, cantidadOfrecida: 2, subtotalItem: 1999 }, 80)).toBe(1999)
  })

  it('el precio total suma lo que se ofrece, no la cantidad del pliego', () => {
    // 2 × 1000 + 15 × 14000 = 2.000 + 210.000
    expect(calcularPrecioTotalCotizacion(conOtraPresentacion, licitacion)).toBe(212000)
    expect(calcularTotalConFlete({ ...conOtraPresentacion, flete: 5000 }, licitacion)).toBe(217000)
  })

  it('un ítem que no está en el pliego sigue sin sumar, aunque traiga cantidad ofrecida', () => {
    const total = calcularPrecioTotalCotizacion(
      { items: [{ articuloId: 'art-otro', precioUnitario: 100, cantidadOfrecida: 5 }] },
      licitacion,
    )
    expect(total).toBe(0)
  })

  it('el desglose informa lo ofrecido, el subtotal real y la cobertura parcial', () => {
    const [cemento, arena] = desglosarCotizacion(conOtraPresentacion, licitacion)

    expect(cemento).toMatchObject({
      cantidad: 80,
      precioUnitario: 1000,
      subtotal: 2000,
      presentacionOfrecida: 'bolsa 25 kg',
      cantidadOfrecida: 2,
      coberturaParcial: true,
    })
    expect(arena.subtotal).toBe(210000)
    expect(arena.presentacionOfrecida).toBeUndefined()
    expect(arena.coberturaParcial).toBeUndefined()
  })

  it('detecta la presentación distinta y la cobertura parcial', () => {
    expect(tienePresentacionDistinta(conOtraPresentacion)).toBe(true)
    expect(tienePresentacionDistinta({ items: [{ articuloId: 'a', precioUnitario: 1, presentacionOfrecida: '  ' }] })).toBe(false)
    expect(tienePresentacionDistinta({ items: [{ articuloId: 'a', precioUnitario: 1 }] })).toBe(false)

    expect(tieneCoberturaParcial(conOtraPresentacion)).toBe(true)
    expect(tieneCoberturaParcial({ items: [], coberturaParcial: true })).toBe(true)
    expect(tieneCoberturaParcial({ items: [{ articuloId: 'a', precioUnitario: 1 }] })).toBe(false)
  })
})

describe('ordenarCotizaciones', () => {
  const pliego = { items: [{ articuloId: 'art-cemento', cantidad: 1, descripcion: 'Cemento', unidadMedida: 'u' }] }
  function oferta(
    id: string,
    puntajeTotal: number,
    extra: Partial<CotizacionResponseDto> = {},
  ): CotizacionResponseDto {
    return {
      cotizacionId: id,
      licitacionId: 'lic-1',
      proveedorId: `prov-${id}`,
      flete: 0,
      plazoEntregaDias: 5,
      fechaCreacion: '2026-11-01T10:00:00Z',
      items: [],
      puntajeTotal,
      esGanadora: false,
      ...extra,
    } as CotizacionResponseDto
  }

  it('ordena por puntaje descendente y usa el ID para mantener orden determinista', () => {
    const lista = [oferta('a', 40), oferta('b', 90), oferta('c', 65)]
    expect(ordenarCotizaciones(lista, 'mejor-puntaje', pliego).map((o) => o.cotizacionId)).toEqual(['b', 'c', 'a'])
  })

  it.each([
    ['precio-asc', ['a', 'b', 'c']],
    ['precio-desc', ['c', 'b', 'a']],
    ['flete-asc', ['a', 'b', 'c']],
    ['flete-desc', ['c', 'b', 'a']],
    ['plazo-asc', ['a', 'b', 'c']],
    ['plazo-desc', ['c', 'b', 'a']],
    ['calificacion-desc', ['c', 'b', 'a']],
    ['calificacion-asc', ['a', 'b', 'c']],
  ] as const)('ordena %s con ambos sentidos y desempata por puntaje', (orden, esperado) => {
    const lista = [
      oferta('b', 80, { items: [{ articuloId: 'art-cemento', precioUnitario: 1000 }], flete: 20, plazoEntregaDias: 5, calificacionProveedor: 3 }),
      oferta('c', 90, { items: [{ articuloId: 'art-cemento', precioUnitario: 2000 }], flete: 30, plazoEntregaDias: 8, calificacionProveedor: 5 }),
      oferta('a', 70, { items: [{ articuloId: 'art-cemento', precioUnitario: 500 }], flete: 10, plazoEntregaDias: 2, calificacionProveedor: 1 }),
    ]
    expect(ordenarCotizaciones(lista, orden, pliego).map((o) => o.cotizacionId)).toEqual(esperado)
  })

  it('deja sin calificación al final en ambos sentidos', () => {
    const lista = [
      oferta('sin', 100, { calificacionProveedor: null }),
      oferta('baja', 50, { calificacionProveedor: 2 }),
      oferta('alta', 70, { calificacionProveedor: 5 }),
    ]
    expect(ordenarCotizaciones(lista, 'calificacion-desc', pliego).map((o) => o.cotizacionId)).toEqual(['alta', 'baja', 'sin'])
    expect(ordenarCotizaciones(lista, 'calificacion-asc', pliego).map((o) => o.cotizacionId)).toEqual(['baja', 'alta', 'sin'])
  })

  it.each([
    'precio-asc', 'precio-desc', 'flete-asc', 'flete-desc', 'plazo-asc', 'plazo-desc',
    'calificacion-desc', 'calificacion-asc',
  ] as const)('desempata %s usando puntaje y después ID', (orden) => {
    const iguales = [
      oferta('z', 70, { subtotalItems: 100, items: [{ articuloId: 'art-cemento', precioUnitario: 100 }], flete: 10, plazoEntregaDias: 5, calificacionProveedor: 3 }),
      oferta('b', 90, { subtotalItems: 100, items: [{ articuloId: 'art-cemento', precioUnitario: 100 }], flete: 10, plazoEntregaDias: 5, calificacionProveedor: 3 }),
      oferta('a', 90, { subtotalItems: 100, items: [{ articuloId: 'art-cemento', precioUnitario: 100 }], flete: 10, plazoEntregaDias: 5, calificacionProveedor: 3 }),
    ]
    expect(ordenarCotizaciones(iguales, orden, pliego).map((o) => o.cotizacionId)).toEqual(['a', 'b', 'z'])
  })

  it('desempata por puntaje y después por ID y no modifica la lista original', () => {
    const lista = [oferta('b', 20), oferta('z', 20), oferta('a', 10)]

    ordenarCotizaciones(lista, 'mejor-puntaje', pliego)

    expect(lista.map((o) => o.cotizacionId)).toEqual(['b', 'z', 'a'])
  })
})

describe('estrellasDePuntaje', () => {
  it('cada estrella son 20 puntos, redondeando', () => {
    expect(estrellasDePuntaje(100)).toBe(5)
    expect(estrellasDePuntaje(97.5)).toBe(5)
    expect(estrellasDePuntaje(70)).toBe(4) // 3,5 redondea hacia arriba
    expect(estrellasDePuntaje(50)).toBe(3)
    expect(estrellasDePuntaje(29)).toBe(1)
    expect(estrellasDePuntaje(9)).toBe(0)
    expect(estrellasDePuntaje(0)).toBe(0)
  })

  it('nunca se sale de 0 a 5', () => {
    expect(estrellasDePuntaje(130)).toBe(5)
    expect(estrellasDePuntaje(-20)).toBe(0)
    expect(estrellasDePuntaje(Number.NaN)).toBe(0)
  })
})
