import { describe, expect, it } from 'vitest'
import { detectarSeparador, MAX_FILAS, parsearCsv, PLANTILLA_CSV, validarCsv } from './csvInsumos'

const ENCABEZADO = 'codigo;descripcion;unidadMedida;moneda;precioReferencia;activo'

describe('parsearCsv', () => {
  it('detecta el separador por el encabezado: ";" (Excel en español) o ","', () => {
    expect(detectarSeparador('a;b;c\r\n1;2;3')).toBe(';')
    expect(detectarSeparador('a,b,c\n1,2,3')).toBe(',')
  })

  it('respeta las comillas: el separador y los saltos de línea adentro no parten el campo', () => {
    expect(parsearCsv('a;b\n"x;y";"dice ""hola""\nsigue"\n')).toEqual([
      ['a', 'b'],
      ['x;y', 'dice "hola"\nsigue'],
    ])
  })

  it('entiende saltos de línea de Windows (CRLF) y el último renglón sin salto', () => {
    expect(parsearCsv('a;b\r\n1;2\r\n3;4')).toEqual([
      ['a', 'b'],
      ['1', '2'],
      ['3', '4'],
    ])
  })
})

describe('validarCsv', () => {
  it('la plantilla descargable trae solo los encabezados: subida tal cual no carga ningún insumo', () => {
    expect(PLANTILLA_CSV.trim()).toBe('Código;Descripción;Unidad de medida;Moneda;Precio de referencia;Activo')
    const resultado = validarCsv(PLANTILLA_CSV)
    expect(resultado.insumos).toEqual([])
    expect(resultado.errorGeneral).toContain('no tiene filas')
  })

  it('los encabezados de la plantilla se reconocen: completada, la plantilla se lee bien', () => {
    const resultado = validarCsv(`${PLANTILLA_CSV}MAT-1;Cemento;BOLSA;ARS;1.500,50;si\r\n`)
    expect(resultado.errorGeneral).toBeNull()
    expect(resultado.errores).toEqual([])
    expect(resultado.insumos[0].datos).toEqual({
      codigo: 'MAT-1',
      descripcion: 'Cemento',
      unidadMedida: 'BOLSA',
      moneda: 'ARS',
      precioReferencia: 1500.5,
      activo: true,
    })
  })

  it('convierte el precio en formato argentino y el activo si/no, y pasa la moneda en mayúsculas', () => {
    const resultado = validarCsv(
      `${ENCABEZADO}\nMAT-1;Cemento;BOLSA;ars;1.500,50;no\nMAT-2;Cal;KG;;;`,
    )
    expect(resultado.insumos[0].datos).toEqual({
      codigo: 'MAT-1',
      descripcion: 'Cemento',
      unidadMedida: 'BOLSA',
      moneda: 'ARS',
      precioReferencia: 1500.5,
      activo: false,
    })
    // Lo que viene vacío no se manda: el servidor usa sus valores por defecto (ARS y activo).
    expect(resultado.insumos[1].datos).toEqual({ codigo: 'MAT-2', descripcion: 'Cal', unidadMedida: 'KG' })
  })

  it('acepta el archivo con coma como separador, BOM y encabezados con otro formato', () => {
    const resultado = validarCsv('\uFEFFCódigo,Descripción,Unidad de medida\nMAT-1,Cemento,BOLSA')
    expect(resultado.errorGeneral).toBeNull()
    expect(resultado.insumos).toHaveLength(1)
    expect(resultado.insumos[0].datos.unidadMedida).toBe('BOLSA')
  })

  it('si faltan columnas obligatorias, no carga nada y dice cuáles', () => {
    const resultado = validarCsv('codigo;descripcion\nMAT-1;Cemento')
    expect(resultado.errorGeneral).toContain('Unidad de medida')
    expect(resultado.insumos).toEqual([])
  })

  it('informa por fila los errores de formato, con el número de fila de la planilla', () => {
    const resultado = validarCsv(
      `${ENCABEZADO}\n;Sin código;BOLSA;;;\nMAT-2;Sin unidad;;;;\nMAT-3;Precio malo;KG;;abc;\nMAT-4;Activo raro;KG;;;quizás\nMAT-5;Bien;KG;;;`,
    )
    expect(resultado.errores.map((e) => e.fila)).toEqual([2, 3, 4, 5])
    expect(resultado.errores[0].motivo).toContain('Falta el código')
    expect(resultado.errores[1].motivo).toContain('Falta la unidad de medida')
    expect(resultado.errores[2].motivo).toContain('precio no es válido')
    expect(resultado.errores[3].motivo).toContain('activo')
    expect(resultado.insumos.map((i) => i.fila)).toEqual([6])
  })

  it('marca como error el código repetido dentro del archivo (sin distinguir mayúsculas)', () => {
    const resultado = validarCsv(`${ENCABEZADO}\nMAT-1;Cemento;BOLSA;;;\nmat-1;Otro;KG;;;`)
    expect(resultado.insumos).toHaveLength(1)
    expect(resultado.errores).toEqual([{ fila: 3, motivo: expect.stringContaining('fila 2') }])
  })

  it('ignora las filas completamente vacías', () => {
    const resultado = validarCsv(`${ENCABEZADO}\nMAT-1;Cemento;BOLSA;;;\n;;;;;\n\n`)
    expect(resultado.insumos).toHaveLength(1)
    expect(resultado.errores).toEqual([])
  })

  it('rechaza el archivo vacío, sin filas o con demasiadas filas', () => {
    expect(validarCsv('').errorGeneral).toContain('vacío')
    expect(validarCsv(ENCABEZADO).errorGeneral).toContain('no tiene filas')
    const demasiadas = Array.from({ length: MAX_FILAS + 1 }, (_, i) => `MAT-${i};Insumo;KG;;;`).join('\n')
    expect(validarCsv(`${ENCABEZADO}\n${demasiadas}`).errorGeneral).toContain(`${MAX_FILAS} filas`)
  })
})
