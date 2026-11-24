import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { STORAGE_KEY } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { ESPERA_BUSQUEDA_MS } from '../requisiciones/configuracionBusqueda'
import { NuevaRequisicionPage } from './NuevaRequisicionPage'

const OBRA_ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'
const ARTICULO_CEMENTO_ID = 'c1c2c3c4-0000-0000-0000-000000000001'

function renderPagina() {
  return render(
    <MemoryRouter>
      <NuevaRequisicionPage />
    </MemoryRouter>,
  )
}

const RECHAZADA_ID = 'd1d2d3d4-0000-0000-0000-000000000013'
function renderEdicion() {
  return render(
    <MemoryRouter initialEntries={[`/obra/requisicion/nueva?editar=${RECHAZADA_ID}&volver=%2Fobra%2Frequisiciones`]}>
      <NuevaRequisicionPage />
    </MemoryRouter>,
  )
}

function iniciarSesionCapataz() {
  localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      usuario: {
        id: 'a1b2c3d4-0000-0000-0000-000000000001',
        nombreCompleto: 'Juan Pérez',
        email: 'capataz@obrasync.com',
        rol: 'Capataz',
      },
      token: 'token-jwt-capataz-test',
    }),
  )
  setToken('token-jwt-capataz-test')
}

type Usuario = ReturnType<typeof userEvent.setup>

/** Elige el insumo de una fila como lo hace el capataz: escribe en el buscador de esa fila y toca el resultado. */
async function elegirInsumo(user: Usuario, numero: number, texto: string, resultado: RegExp) {
  await user.type(screen.getByLabelText(`Insumo #${numero}`), texto)
  await user.click(await screen.findByRole('button', { name: resultado }))
}

describe('NuevaRequisicionPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
    localStorage.clear()
    setToken(null)
  })

  it('precarga y reenvía la requisición rechazada con los campos del contrato y muestra el éxito', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    let payload: unknown
    server.use(http.put('*/api/requisiciones/:id', async ({ request }) => {
      payload = await request.json()
      return HttpResponse.json({
        requisicionId: RECHAZADA_ID, numeroRequisicion: 'REQ-2026-0013', obraId: OBRA_ID,
        etapa: 'Fundaciones', solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
        estado: 'Pendiente', observaciones: 'Revisar la cantidad', fechaCreacion: '2026-10-20T12:00:00Z',
        items: [{ articuloId: ARTICULO_CEMENTO_ID, descripcion: 'Cemento Portland Especial 25kg', cantidad: 31, unidadMedida: 'BOLSA' }],
      })
    }))
    renderEdicion()
    expect(await screen.findByRole('heading', { name: 'Editar y reenviar requisición' })).toBeInTheDocument()
    expect(screen.getByLabelText('Obra')).toHaveValue(OBRA_ID)
    expect(screen.getByLabelText('Etapa de la obra')).toHaveValue('Fundaciones')
    expect(screen.getByLabelText('Observaciones (opcional)')).toHaveValue('Revisar la cantidad')
    expect(screen.getByLabelText('Insumo #1')).toHaveValue('Cemento Portland Especial 25kg')
    expect(screen.getByLabelText('Cantidad')).toHaveValue(25)
    await user.clear(screen.getByLabelText('Cantidad'))
    await user.type(screen.getByLabelText('Cantidad'), '31')
    await user.click(screen.getByRole('button', { name: 'Guardar y reenviar' }))
    expect(await screen.findByRole('heading', { name: 'Requisición enviada con éxito' })).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0013')).toBeInTheDocument()
    expect(payload).toEqual({ etapa: 'Fundaciones', observaciones: 'Revisar la cantidad', items: [{ articuloId: ARTICULO_CEMENTO_ID, cantidad: 31 }] })
    expect(screen.getByRole('link', { name: 'Volver al historial' })).toHaveAttribute('href', '/obra/requisiciones')
  })

  it('repetir un pedido anterior precarga los mismos materiales y crea una requisición NUEVA (POST)', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    let payload: Record<string, unknown> | undefined
    let metodoPut = false
    server.use(
      http.put('*/api/requisiciones/:id', () => {
        metodoPut = true
        return HttpResponse.json({}, { status: 500 })
      }),
      http.post('*/api/requisiciones', async ({ request }) => {
        payload = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({
          requisicionId: 'e1e2e3e4-0000-0000-0000-000000000099', numeroRequisicion: 'REQ-2026-0099', obraId: OBRA_ID,
          etapa: 'Fundaciones', solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
          estado: 'Pendiente', observaciones: 'Revisar la cantidad', fechaCreacion: '2026-10-21T12:00:00Z',
          items: [{ articuloId: ARTICULO_CEMENTO_ID, descripcion: 'Cemento Portland Especial 25kg', cantidad: 25, unidadMedida: 'BOLSA' }],
        }, { status: 201 })
      }),
    )
    render(
      <MemoryRouter initialEntries={[`/obra/requisicion/nueva?repetir=${RECHAZADA_ID}`]}>
        <NuevaRequisicionPage />
      </MemoryRouter>,
    )
    expect(await screen.findByRole('heading', { name: 'Repetir pedido' })).toBeInTheDocument()
    expect(screen.getByRole('note')).toHaveTextContent('Cargamos los mismos materiales de tu pedido anterior')
    expect(screen.getByLabelText('Obra')).toHaveValue(OBRA_ID)
    expect(screen.getByLabelText('Insumo #1')).toHaveValue('Cemento Portland Especial 25kg')
    expect(screen.getByLabelText('Cantidad')).toHaveValue(25)

    await user.click(screen.getByRole('button', { name: 'Crear nuevo pedido' }))
    expect(await screen.findByRole('heading', { name: 'Requisición enviada con éxito' })).toBeInTheDocument()
    expect(payload).toMatchObject({ obraId: OBRA_ID, repeticionIntencional: true, items: [{ articuloId: ARTICULO_CEMENTO_ID, cantidad: 25 }] })
    expect(metodoPut).toBe(false)
  })

  it.each([
    [403, 'La requisición no pertenece al Capataz o no tenés permiso para editarla.'],
    [409, 'La requisición ya fue aprobada, rechazada o cancelada y no puede editarse.'],
  ])('informa claramente el error %s al reenviar', async (status, mensaje) => {
    iniciarSesionCapataz()
    server.use(http.put('*/api/requisiciones/:id', () => HttpResponse.json({ title: 'Error', status }, { status })))
    const user = userEvent.setup()
    renderEdicion()
    await screen.findByRole('heading', { name: 'Editar y reenviar requisición' })
    await user.click(screen.getByRole('button', { name: 'Guardar y reenviar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(mensaje)
  })

  it('Caso demo CU-02: carga requisición de 80 bolsas de cemento para "Estructura" y muestra confirmación con "Aprobación Pendiente"', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()

    // Espera que termine la carga inicial de obras
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' }),
    ).toBeInTheDocument()

    // Selecciona Obra "Torre Alvear"
    const selectObra = screen.getByLabelText('Obra')
    await user.selectOptions(selectObra, OBRA_ID)

    // Selecciona Etapa "Estructura"
    const selectEtapa = screen.getByLabelText('Etapa de la obra')
    await user.selectOptions(selectEtapa, 'Estructura')

    // Busca el insumo "Cemento Portland Especial 25kg" y lo elige
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    // Verifica que la unidad de medida se muestra tomada del catálogo (BOLSA)
    expect(screen.getByText('Bolsas')).toBeInTheDocument()

    // Ingresa cantidad 80
    const inputCantidad = screen.getByLabelText('Cantidad')
    await user.type(inputCantidad, '80')

    // Envía la requisición
    const botonEnviar = screen.getByRole('button', { name: 'Enviar requisición' })
    await user.click(botonEnviar)

    // Verifica la tarjeta de confirmación de éxito
    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: 'Requisición enviada con éxito',
      }),
    ).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0001')).toBeInTheDocument()
    expect(screen.getByText('Aprobación Pendiente')).toBeInTheDocument()
    expect(screen.getByText(/Torre Alvear/)).toBeInTheDocument()
    expect(screen.getByText(/Estructura/)).toBeInTheDocument()
    expect(
      screen.getByText(/80 bolsas — Cemento Portland Especial 25kg/),
    ).toBeInTheDocument()
    server.use(http.get('*/api/requisiciones/d1d2d3d4-0000-0000-0000-000000000001', () =>
      HttpResponse.json({
        requisicionId: 'd1d2d3d4-0000-0000-0000-000000000001',
        obraId: OBRA_ID, etapa: 'Estructura', estado: 'Pendiente',
        items: [{ articuloId: ARTICULO_CEMENTO_ID, descripcion: 'Cemento Portland Especial 25kg', cantidad: 80, unidadMedida: 'BOLSA' }],
      }),
    ))
    await user.click(screen.getByRole('link', { name: 'Editar requisición' }))
    expect(await screen.findByRole('heading', { name: 'Editar y reenviar requisición' })).toBeInTheDocument()
    expect(screen.getByLabelText('Cantidad')).toHaveValue(80)
    expect(screen.getByLabelText('Obra')).toHaveValue(OBRA_ID)
  })

  it('muestra validaciones inline sin llamar a la API si faltan campos obligatorios o la cantidad es 0 o vacía', async () => {
    iniciarSesionCapataz()
    let llamadasApi = 0
    server.use(
      http.post('*/api/requisiciones', () => {
        llamadasApi++
        return HttpResponse.json({})
      }),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    // Intento de envío completamente vacío
    const botonEnviar = screen.getByRole('button', { name: 'Enviar requisición' })
    await user.click(botonEnviar)

    expect(screen.getByText('Tenés que seleccionar una obra.')).toBeInTheDocument()
    expect(
      screen.getByText('Tenés que seleccionar una etapa de la obra.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Seleccioná un insumo del catálogo.')).toBeInTheDocument()
    expect(screen.getByText('Ingresá una cantidad.')).toBeInTheDocument()
    expect(llamadasApi).toBe(0)

    // Carga de cantidad en 0
    const inputCantidad = screen.getByLabelText('Cantidad')
    await user.type(inputCantidad, '0')
    await user.click(botonEnviar)

    expect(screen.getByText('La cantidad debe ser mayor a 0.')).toBeInTheDocument()
    expect(llamadasApi).toBe(0)
  })

  it('muestra el detail de ProblemDetails ante una respuesta 400 del servidor', async () => {
    iniciarSesionCapataz()
    server.use(
      http.post('*/api/requisiciones', () =>
        HttpResponse.json(
          {
            type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
            title: 'Bad Request',
            status: 400,
            detail: 'La cantidad del ítem supera el límite permitido.',
          },
          { status: 400 },
        ),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '500')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'La cantidad del ítem supera el límite permitido.',
    )
  })

  it('avisa que no hay permiso ante una respuesta 403 del servidor', async () => {
    iniciarSesionCapataz()
    server.use(
      http.post('*/api/requisiciones', () =>
        HttpResponse.json({ status: 403 }, { status: 403 }),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No tenés permiso para crear requisiciones.')
  })

  it('muestra el detail de ProblemDetails ante una respuesta 404 del servidor', async () => {
    iniciarSesionCapataz()
    server.use(
      http.post('*/api/requisiciones', () =>
        HttpResponse.json({ status: 404, detail: 'La obra no existe.' }, { status: 404 }),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('La obra no existe.')
  })

  it('muestra el detail de ProblemDetails ante una respuesta 409 del servidor', async () => {
    iniciarSesionCapataz()
    server.use(
      http.post('*/api/requisiciones', () =>
        HttpResponse.json({ status: 409, detail: 'La etapa ya está cerrada.' }, { status: 409 }),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('La etapa ya está cerrada.')
  })

  it('muestra un mensaje amigable ante una caída de red', async () => {
    iniciarSesionCapataz()
    server.use(http.post('*/api/requisiciones', () => HttpResponse.error()))

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'No se pudo conectar con el servidor. Intentá nuevamente.',
    )
  })

  it('muestra mensaje de error y botón Reintentar si falla la carga inicial de obras', async () => {
    iniciarSesionCapataz()
    // Simula fallo en la carga de obras
    server.use(http.get('*/api/obras', () => HttpResponse.error()))

    const user = userEvent.setup()
    renderPagina()

    // Se muestra el error en vez de pantalla en blanco
    expect(
      await screen.findByText('No se pudieron cargar los datos necesarios.'),
    ).toBeInTheDocument()
    const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
    expect(botonReintentar).toBeInTheDocument()

    // Ahora se restablece el servicio exitoso y se reintenta
    server.resetHandlers()
    await user.click(botonReintentar)

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' }),
    ).toBeInTheDocument()
  })

  it('permite agregar y quitar filas, y no repite el insumo seleccionado en otras filas', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    // Con una sola fila, el botón Quitar debe estar deshabilitado
    const botonQuitar1 = screen.getByRole('button', { name: 'Quitar insumo 1' })
    expect(botonQuitar1).toBeDisabled()

    // Agrega una segunda fila
    const botonAgregar = screen.getByRole('button', { name: '+ Agregar otro insumo' })
    await user.click(botonAgregar)

    expect(screen.getByLabelText('Insumo #1')).toBeInTheDocument()
    expect(screen.getByLabelText('Insumo #2')).toBeInTheDocument()
    // Al haber más de una fila, ambos botones Quitar quedan habilitados
    expect(botonQuitar1).toBeEnabled()
    const botonQuitar2 = screen.getByRole('button', { name: 'Quitar insumo 2' })
    expect(botonQuitar2).toBeEnabled()

    // Elige Cemento en la primera fila
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    // En la segunda fila, buscar Cemento lo muestra como "ya agregado" y no se puede elegir de nuevo
    await user.type(screen.getByLabelText('Insumo #2'), 'cemento')
    expect(await screen.findByRole('button', { name: /MAT-CEM-001.*ya agregado/ })).toBeDisabled()

    // Quita la segunda fila
    await user.click(botonQuitar2)

    expect(screen.queryByLabelText('Insumo #2')).not.toBeInTheDocument()
    // Vuelve a quedar una sola fila, por lo que el botón Quitar vuelve a estar deshabilitado
    expect(botonQuitar1).toBeDisabled()
  })

  it('el body enviado a POST /api/requisiciones tiene obraId, etapa como texto e items con cantidad numérica', async () => {
    iniciarSesionCapataz()
    let cuerpoCapturado: {
      obraId?: string
      etapa?: string
      observaciones?: string | null
      items?: Array<{ articuloId: string; cantidad: number }>
    } | null = null

    server.use(
      http.post('*/api/requisiciones', async ({ request }) => {
        cuerpoCapturado = (await request.json()) as typeof cuerpoCapturado
        return HttpResponse.json(
          {
            requisicionId: 'd1d2d3d4-0000-0000-0000-000000000001',
            numeroRequisicion: 'REQ-2026-0001',
            obraId: cuerpoCapturado?.obraId,
            etapa: cuerpoCapturado?.etapa,
            solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
            aprobadorId: null,
            estado: 'Pendiente',
            observaciones: cuerpoCapturado?.observaciones ?? null,
            fechaCreacion: new Date().toISOString(),
            fechaAprobacion: null,
            items: [
              {
                articuloId: ARTICULO_CEMENTO_ID,
                descripcion: 'Cemento Portland Especial 25kg',
                cantidad: 80,
                unidadMedida: 'BOLSA',
              },
            ],
          },
          { status: 201 },
        )
      }),
    )

    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.type(
      screen.getByLabelText('Observaciones (opcional)'),
      'Entrega urgente en acopio',
    )

    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    await waitFor(() => {
      expect(cuerpoCapturado).toEqual({
        obraId: OBRA_ID,
        etapa: 'Estructura',
        observaciones: 'Entrega urgente en acopio',
        items: [
          {
            articuloId: ARTICULO_CEMENTO_ID,
            cantidad: 80,
          },
        ],
      })
    })
  })
})

describe('NuevaRequisicionPage - buscador de insumos en cada fila', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  async function abrirPagina() {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })
    return user
  }

  it('no hay buscador arriba de las filas ni desplegable: cada insumo se busca en su propia fila', async () => {
    await abrirPagina()

    expect(screen.queryByLabelText('Buscar insumo')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Insumo #1')).toHaveAttribute('type', 'search')
    expect(screen.queryByRole('combobox', { name: 'Insumo #1' })).not.toBeInTheDocument()
  })

  it('al escribir en el buscador de la fila quedan solo los insumos que coinciden', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'arena')

    expect(await screen.findByRole('button', { name: /Arena Fina/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Cemento/ })).not.toBeInTheDocument()
  })

  it('le pide al servidor la búsqueda con el parámetro search', async () => {
    const pedidos: (string | null)[] = []
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        pedidos.push(new URL(request.url).searchParams.get('search'))
        return undefined // sigue con el mock de siempre
      }),
    )
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'cal')

    await waitFor(() => expect(pedidos).toContain('cal'))
  })

  it('no pide el catálogo entero al abrir la pantalla: solo busca cuando se escribe', async () => {
    const pedidos: (string | null)[] = []
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        pedidos.push(new URL(request.url).searchParams.get('search'))
        return undefined
      }),
    )
    await abrirPagina()

    expect(pedidos).toEqual([])
  })

  it('al elegir un resultado la fila muestra el insumo con su unidad y ofrece "Cambiar"', async () => {
    const user = await abrirPagina()

    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    expect(screen.getByLabelText('Insumo #1')).toHaveValue('MAT-CEM-001 — Cemento Portland Especial 25kg')
    expect(screen.getByLabelText('Insumo #1')).toHaveAttribute('readonly')
    expect(screen.getByText('Bolsas')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /MAT-CEM-001/ })).not.toBeInTheDocument()
  })

  it('"Cambiar" vuelve a la búsqueda de esa fila, con el cursor en el campo', async () => {
    const user = await abrirPagina()
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    await user.click(screen.getByRole('button', { name: 'Cambiar el insumo 1' }))

    const campo = screen.getByLabelText('Insumo #1')
    expect(campo).toHaveAttribute('type', 'search')
    expect(campo).toHaveValue('')
    expect(campo).toHaveFocus()
    expect(screen.queryByText('Bolsas')).not.toBeInTheDocument()
  })

  it('al borrar lo escrito desaparecen los resultados', async () => {
    const user = await abrirPagina()
    const campo = screen.getByLabelText('Insumo #1')
    await user.type(campo, 'arena')
    await screen.findByRole('button', { name: /Arena Fina/ })

    await user.clear(campo)

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /Arena Fina/ })).not.toBeInTheDocument(),
    )
  })

  it('apretar Enter en el buscador no envía el formulario', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'cal{Enter}')

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('avisa si la búsqueda falla', async () => {
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        if (new URL(request.url).searchParams.get('search')) {
          return HttpResponse.json({ title: 'Error' }, { status: 500 })
        }
        return undefined
      }),
    )
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'x')

    expect(
      await screen.findByText('No se pudo buscar insumos. Intentá de nuevo.'),
    ).toBeInTheDocument()
  })

  it('muestra los resultados debajo del campo, con la cantidad encontrada', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'cemento')

    expect(await screen.findByText(/1 insumo encontrado\./)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /MAT-CEM-001/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /MAT-ARE-001/ })).not.toBeInTheDocument()
  })

  it('cada fila tiene su propio buscador: elegir en la segunda no toca la primera', async () => {
    const user = await abrirPagina()
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.click(screen.getByRole('button', { name: '+ Agregar otro insumo' }))

    await elegirInsumo(user, 2, 'arena', /MAT-ARE-001/)

    expect(screen.getByLabelText('Insumo #1')).toHaveValue('MAT-CEM-001 — Cemento Portland Especial 25kg')
    expect(screen.getByLabelText('Insumo #2')).toHaveValue('MAT-ARE-001 — Arena Fina a Granel')
  })

  it('un insumo elegido en otra fila figura como "ya agregado" y no se puede elegir de nuevo', async () => {
    const user = await abrirPagina()
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.click(screen.getByRole('button', { name: '+ Agregar otro insumo' }))

    await user.type(screen.getByLabelText('Insumo #2'), 'cemento')

    expect(await screen.findByRole('button', { name: /MAT-CEM-001.*ya agregado/ })).toBeDisabled()
  })

  it('muestra el error de la fila si se envía sin elegir un insumo y lo quita al elegirlo', async () => {
    const user = await abrirPagina()
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))
    expect(screen.getByText('Seleccioná un insumo del catálogo.')).toBeInTheDocument()
    expect(screen.getByLabelText('Insumo #1')).toBeInvalid()

    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    expect(screen.queryByText('Seleccioná un insumo del catálogo.')).not.toBeInTheDocument()
  })
})

describe('NuevaRequisicionPage - búsqueda con espera (#58)', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  async function abrirPagina() {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })
    return user
  }

  it('no pide en cada letra: espera una pausa antes de llamar al servidor', async () => {
    const pedidos: string[] = []
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        const texto = new URL(request.url).searchParams.get('search')
        if (texto) pedidos.push(texto)
        return undefined // sigue con el mock de siempre
      }),
    )
    await abrirPagina()

    // Desde acá el reloj es simulado: el tiempo solo avanza cuando lo decimos.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const buscador = screen.getByLabelText('Insumo #1')
    fireEvent.change(buscador, { target: { value: 'c' } })
    fireEvent.change(buscador, { target: { value: 'ca' } })
    fireEvent.change(buscador, { target: { value: 'cal' } })

    expect(pedidos).toEqual([]) // todavía no pasó la pausa

    await act(async () => {
      await vi.advanceTimersByTimeAsync(ESPERA_BUSQUEDA_MS)
    })

    // Vuelve el reloj real para esperar la respuesta del servidor simulado.
    vi.useRealTimers()
    await waitFor(() => expect(pedidos).toEqual(['cal'])) // una sola llamada, no tres
  })

  it('avisa cuando no hay insumos que coincidan', async () => {
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        if (new URL(request.url).searchParams.get('search')) {
          return HttpResponse.json([])
        }
        return undefined
      }),
    )
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'zzz')

    expect(
      await screen.findByText('No encontramos insumos con ese nombre'),
    ).toBeInTheDocument()
  })

  it('el botón Limpiar borra la búsqueda y devuelve el foco al campo', async () => {
    const user = await abrirPagina()
    const buscador = screen.getByLabelText('Insumo #1')
    expect(screen.queryByRole('button', { name: 'Limpiar la búsqueda del insumo 1' })).not.toBeInTheDocument()

    await user.type(buscador, 'arena')
    await screen.findByRole('button', { name: /Arena Fina/ })

    await user.click(screen.getByRole('button', { name: 'Limpiar la búsqueda del insumo 1' }))

    expect(buscador).toHaveValue('')
    expect(buscador).toHaveFocus()
    expect(screen.queryByRole('button', { name: /Arena Fina/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Limpiar la búsqueda del insumo 1' })).not.toBeInTheDocument()
  })

  it('ignora la respuesta de una búsqueda vieja si ya salió otra', async () => {
    server.use(
      http.get('*/api/catalogo/insumos', async ({ request }) => {
        if (new URL(request.url).searchParams.get('search') === 'ce') {
          await delay(800) // esta contesta tarde, cuando ya se buscó otra cosa
          return HttpResponse.json([
            {
              articuloId: ARTICULO_CEMENTO_ID,
              codigo: 'MAT-CEM-001',
              descripcion: 'Cemento Portland Especial 25kg',
              unidadMedida: 'BOLSA',
              moneda: 'ARS',
              activo: true,
            },
          ])
        }
        return undefined
      }),
    )
    const user = await abrirPagina()
    const buscador = screen.getByLabelText('Insumo #1')

    await user.type(buscador, 'ce')
    await new Promise((r) => setTimeout(r, ESPERA_BUSQUEDA_MS + 100)) // sale la consulta lenta
    await user.clear(buscador)
    await user.type(buscador, 'ar')

    expect(await screen.findByRole('button', { name: /Arena Fina/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Cemento/ })).not.toBeInTheDocument()

    await new Promise((r) => setTimeout(r, 900)) // llega la respuesta vieja

    expect(screen.queryByRole('button', { name: /Cemento/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Arena Fina/ })).toBeInTheDocument()
  }, 15000)
})

describe('NuevaRequisicionPage - idempotencia y protección contra doble envío (#120)', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('envía el encabezado Idempotency-Key y reutiliza el mismo valor si se reintenta el mismo envío tras un fallo de red', async () => {
    const keysRecibidas: Array<string | null> = []
    let intento = 0

    server.use(
      http.post('*/api/requisiciones', ({ request }) => {
        keysRecibidas.push(request.headers.get('Idempotency-Key'))
        intento++
        if (intento === 1) {
          return HttpResponse.error()
        }
        return HttpResponse.json(
          {
            requisicionId: 'd1d2d3d4-0000-0000-0000-000000000001',
            numeroRequisicion: 'REQ-2026-0001',
            obraId: OBRA_ID,
            etapa: 'Estructura',
            solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
            aprobadorId: null,
            estado: 'Pendiente',
            observaciones: null,
            fechaCreacion: new Date().toISOString(),
            fechaAprobacion: null,
            items: [
              {
                articuloId: ARTICULO_CEMENTO_ID,
                descripcion: 'Cemento Portland Especial 25kg',
                cantidad: 80,
                unidadMedida: 'BOLSA',
              },
            ],
          },
          { status: 201 },
        )
      }),
    )

    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })
    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await user.selectOptions(screen.getByLabelText('Etapa de la obra'), 'Estructura')
    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    await user.type(screen.getByLabelText('Cantidad'), '80')

    // Primer intento falla por red
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/No se pudo conectar/)

    // Segundo intento sin cambiar datos reutiliza exactamente el mismo Idempotency-Key
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))
    expect(
      await screen.findByRole('heading', { name: 'Requisición enviada con éxito' }),
    ).toBeInTheDocument()

    expect(keysRecibidas).toHaveLength(2)
    expect(keysRecibidas[0]).toBeTruthy()
    expect(keysRecibidas[1]).toBe(keysRecibidas[0])
  })
})

describe('NuevaRequisicionPage - paginación de insumos (#172)', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  function insumoDePrueba(n: number) {
    return {
      articuloId: `c1c2c3c4-0000-0000-0000-00000000010${n}`,
      codigo: `MAT-PRU-00${n}`,
      descripcion: `Insumo de prueba ${n}`,
      unidadMedida: 'BOLSA',
      moneda: 'ARS',
      activo: true,
    }
  }

  /** Simula un servicio con 3 insumos que entrega de a 2. Anota los pedidos. */
  function tresInsumosDeADos(pedidos: { search: string | null; pagina: string }[] = []) {
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        const url = new URL(request.url)
        const pagina = url.searchParams.get('pagina') ?? '1'
        pedidos.push({ search: url.searchParams.get('search'), pagina })
        const todos = [1, 2, 3].map(insumoDePrueba)
        const filas = pagina === '1' ? todos.slice(0, 2) : todos.slice(2)
        return HttpResponse.json(filas, { headers: { 'X-Total-Count': '3' } })
      }),
    )
  }

  async function abrirPagina() {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })
    return user
  }

  it('la búsqueda muestra la primera página y "Ver más" trae el resto', async () => {
    tresInsumosDeADos()
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'prueba')

    expect(await screen.findByText('Mostrando 2 de 3 insumos.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Insumo de prueba 3/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    expect(await screen.findByRole('button', { name: /Insumo de prueba 3/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ver más' })).not.toBeInTheDocument()
  })

  it('"Ver más" pide la página siguiente con el mismo texto', async () => {
    const pedidos: { search: string | null; pagina: string }[] = []
    tresInsumosDeADos(pedidos)
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Insumo #1'), 'prueba')
    await screen.findByText('2 insumos encontrados. Tocá uno para elegirlo.')

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    await screen.findByText('3 insumos encontrados. Tocá uno para elegirlo.')
    expect(pedidos.at(-1)).toEqual({ search: 'prueba', pagina: '2' })
  })

  it('un insumo de la segunda página se elige bien y queda con su unidad', async () => {
    tresInsumosDeADos()
    const user = await abrirPagina()
    await user.type(screen.getByLabelText('Insumo #1'), 'prueba')
    await screen.findByText('2 insumos encontrados. Tocá uno para elegirlo.')
    await user.click(screen.getByRole('button', { name: 'Ver más' }))
    await screen.findByText('3 insumos encontrados. Tocá uno para elegirlo.')

    await user.click(screen.getByRole('button', { name: /Insumo de prueba 3/ }))

    expect(screen.getByLabelText('Insumo #1')).toHaveValue('MAT-PRU-003 — Insumo de prueba 3')
    expect(screen.getByText('Bolsas')).toBeInTheDocument()
  })

  it('si falla "Ver más" avisa y deja reintentar', async () => {
    let fallar = true
    server.use(
      http.get('*/api/catalogo/insumos', ({ request }) => {
        const pagina = new URL(request.url).searchParams.get('pagina') ?? '1'
        if (pagina === '1') {
          return HttpResponse.json([insumoDePrueba(1), insumoDePrueba(2)], {
            headers: { 'X-Total-Count': '3' },
          })
        }
        if (fallar) return HttpResponse.json({ title: 'Error' }, { status: 500 })
        return HttpResponse.json([insumoDePrueba(3)], { headers: { 'X-Total-Count': '3' } })
      }),
    )
    const user = await abrirPagina()
    await user.type(screen.getByLabelText('Insumo #1'), 'prueba')

    await user.click(await screen.findByRole('button', { name: 'Ver más' }))
    expect(await screen.findByText('No se pudieron cargar más. Intentá nuevamente.')).toBeInTheDocument()

    fallar = false
    await user.click(screen.getByRole('button', { name: 'Ver más' }))
    expect(await screen.findByRole('button', { name: /Insumo de prueba 3/ })).toBeInTheDocument()
  })

  // Cargar varios insumos seguidos sin tocar el mouse ni subir al principio (#237).
  it('al elegir un insumo el cursor pasa a su cantidad', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)

    expect(screen.getAllByRole('spinbutton')[0]).toHaveFocus()
  })

  it('"+ Agregar otro insumo" deja el cursor en el buscador de la fila nueva', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.click(screen.getByRole('button', { name: '+ Agregar otro insumo' }))

    expect(screen.getByLabelText('Insumo #2')).toHaveFocus()
  })

  it('Enter en el buscador elige el primer resultado disponible', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await user.type(screen.getByLabelText('Insumo #1'), 'cemento')
    await screen.findByRole('button', { name: /MAT-CEM-001/ })
    await user.keyboard('{Enter}')

    expect(await screen.findByRole('button', { name: 'Cambiar el insumo 1' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('Enter en la cantidad agrega la fila siguiente solo si hay insumo y cantidad', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()
    await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' })

    await elegirInsumo(user, 1, 'cemento', /MAT-CEM-001/)
    // Sin cantidad: no agrega nada ni envía el formulario.
    await user.keyboard('{Enter}')
    expect(screen.queryByLabelText('Insumo #2')).not.toBeInTheDocument()

    await user.type(screen.getAllByRole('spinbutton')[0], '5{Enter}')

    expect(screen.getByLabelText('Insumo #2')).toHaveFocus()
  })
})
