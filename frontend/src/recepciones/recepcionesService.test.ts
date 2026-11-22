import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/ApiError'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import {
  descargarFotoEvidencia,
  obtenerOrdenesPendientes,
  obtenerRecepcionesDeOrden,
  registrarEntregaConProblema,
  TIEMPO_LIMITE_SUBIDA_FOTOS_MS,
} from './recepcionesService'

// OJO con las fotos en estas pruebas: dentro de jsdom el FormData y el Blob son los de jsdom, y el fetch de Node
// no sabe serializarlos (falla con "Request body length does not match content-length"). En un navegador real
// no pasa. Por eso las pruebas con fotos NO mandan el pedido por MSW: reemplazan fetch y revisan el FormData que
// arma el servicio (ver "arma el multipart del contrato").
function foto(tamano: number): Blob {
  return new Blob([new Uint8Array(tamano)], { type: 'image/jpeg' })
}

/** La primera orden pendiente de la demo (despachada, con fecha estimada y observación). */
async function primeraOrden() {
  const [orden] = await obtenerOrdenesPendientes()
  return orden
}

beforeEach(() => {
  reiniciarRecepcionesMock()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('registrarEntregaConProblema', () => {
  it('registra la entrega con problema en estado Rechazada', async () => {
    const orden = await primeraOrden()

    const registrada = await registrarEntregaConProblema(
      { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'Llegó dañada', detalle: '3 bolsas rotas' },
      [],
    )

    expect(registrada.estado).toBe(4)
    expect(registrada.motivoProblema).toBe('Llegó dañada')
    expect(registrada.observaciones).toBe('3 bolsas rotas')
    expect(registrada.fotos).toEqual([])
    // Sin número de remito, el servicio genera uno interno.
    expect(registrada.numeroRemito).toMatch(/^PROBLEMA-/)
  })

  it('arma el multipart del contrato: un "fotos" por cada foto, y omite los opcionales vacíos', async () => {
    const orden = await primeraOrden()
    const fetchFalso = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify({ id: 'x', estado: 4 }), { status: 201, headers: { 'Content-Type': 'application/json' } }))

    await registrarEntregaConProblema(
      { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'No llegó nada', detalle: '   ', numeroRemito: '' },
      [foto(1500), foto(2500)],
    )

    expect(fetchFalso).toHaveBeenCalledTimes(1)
    const [url, opciones] = fetchFalso.mock.calls[0]
    expect(String(url)).toMatch(/\/api\/recepciones\/problemas$/)
    expect(opciones?.method).toBe('POST')
    // Con FormData NO se pone Content-Type a mano: el navegador lo arma solo, con el "boundary" que separa las partes.
    expect(new Headers(opciones?.headers).has('Content-Type')).toBe(false)

    const formulario = opciones?.body as FormData
    const campos = Object.fromEntries([...formulario.entries()].filter(([, valor]) => typeof valor === 'string'))
    expect(campos).toEqual({ obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'No llegó nada' })
    const archivos = formulario.getAll('fotos') as File[]
    expect(archivos.map((archivo) => [archivo.name, archivo.size])).toEqual([
      ['foto-1.jpg', 1500],
      ['foto-2.jpg', 2500],
    ])
  })

  it('manda el detalle y el remito sin los espacios de los costados', async () => {
    const orden = await primeraOrden()
    let campos: Record<string, string> = {}
    server.use(
      http.post('*/api/recepciones/problemas', async ({ request }) => {
        const formulario = await request.formData()
        campos = Object.fromEntries([...formulario.entries()].filter(([, v]) => typeof v === 'string')) as Record<string, string>
        return HttpResponse.json({ id: 'x', estado: 4 }, { status: 201 })
      }),
    )

    await registrarEntregaConProblema(
      { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'No es lo pedido', detalle: '  vino hierro del 8  ', numeroRemito: ' R-77 ' },
      [],
    )

    expect(campos.detalle).toBe('vino hierro del 8')
    expect(campos.numeroRemito).toBe('R-77')
  })

  it('no cambia lo recibido: la orden sigue pendiente con el mismo saldo y queda con problema abierto', async () => {
    const antes = await primeraOrden()

    await registrarEntregaConProblema({ obraId: antes.obraId, ordenCompraId: antes.ordenCompraId, motivo: 'Llegó incompleta' }, [])

    const despues = (await obtenerOrdenesPendientes()).find((o) => o.ordenCompraId === antes.ordenCompraId)
    expect(despues).toBeDefined()
    expect(despues?.items.map((i) => i.cantidadRecibida)).toEqual(antes.items.map((i) => i.cantidadRecibida))
    expect(despues?.tieneProblemaAbierto).toBe(true)
  })

  it('devuelve el error del servicio si el pedido no sirve (400)', async () => {
    const orden = await primeraOrden()

    const error = await registrarEntregaConProblema(
      { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'Otro' as never },
      [],
    ).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).detail).toMatch(/motivos/)
  })

  it('le da hasta 1 minuto a la subida, más que un pedido normal', () => {
    expect(TIEMPO_LIMITE_SUBIDA_FOTOS_MS).toBe(60_000)
  })
})

describe('obtenerRecepcionesDeOrden', () => {
  it('trae el despacho, el saldo y el historial con el problema', async () => {
    const orden = await primeraOrden()
    await registrarEntregaConProblema(
      { obraId: orden.obraId, ordenCompraId: orden.ordenCompraId, motivo: 'Llegó dañada', detalle: 'rotas' },
      [],
    )

    const detalle = await obtenerRecepcionesDeOrden(orden.ordenCompraId)

    expect(detalle.ordenCompraId).toBe(orden.ordenCompraId)
    expect(detalle.fechaEstimadaLlegada).toBe('2026-11-23T12:00:00Z')
    expect(detalle.observacionDespacho).toMatch(/camión chico/)
    expect(detalle.estaCompleta).toBe(false)
    expect(detalle.tieneProblemaAbierto).toBe(true)
    expect(detalle.items.length).toBeGreaterThan(0)
    expect(detalle.recepciones).toHaveLength(1)
    expect(detalle.recepciones[0].estado).toBe(4)
    expect(detalle.recepciones[0].motivoProblema).toBe('Llegó dañada')
  })

  it('una orden sin movimientos viene con el historial vacío y sin problema', async () => {
    const orden = await primeraOrden()

    const detalle = await obtenerRecepcionesDeOrden(orden.ordenCompraId)

    expect(detalle.recepciones).toEqual([])
    expect(detalle.tieneProblemaAbierto).toBe(false)
  })

  it('responde 404 si la orden no existe', async () => {
    const error = await obtenerRecepcionesDeOrden('00000000-0000-0000-0000-000000000000').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
  })
})

describe('descargarFotoEvidencia', () => {
  it('pide una imagen (no JSON) y devuelve los bytes que manda el servidor', async () => {
    let accept: string | null = null
    server.use(
      http.get('*/api/recepciones/:id/fotos/:fotoId', ({ request }) => {
        accept = request.headers.get('Accept')
        return new HttpResponse(new Uint8Array([1, 2, 3]), { status: 200, headers: { 'Content-Type': 'image/png' } })
      }),
    )

    const imagen = await descargarFotoEvidencia('r1', 'f1')

    expect(accept).toBe('image/*')
    expect(imagen.size).toBe(3)
    expect(imagen.type).toBe('image/png')
  })

  it('responde 404 si la foto no existe', async () => {
    const error = await descargarFotoEvidencia('no-existe', 'tampoco').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
  })

  it('falla con un error claro si el servidor no devuelve la imagen', async () => {
    server.use(http.get('*/api/recepciones/:id/fotos/:fotoId', () => new HttpResponse(null, { status: 204 })))

    const error = await descargarFotoEvidencia('r1', 'f1').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
  })
})
