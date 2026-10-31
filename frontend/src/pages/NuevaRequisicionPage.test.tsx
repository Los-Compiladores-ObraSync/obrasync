import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { STORAGE_KEY } from '../auth/sessionContext'
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

describe('NuevaRequisicionPage', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('Caso demo CU-02: carga requisición de 80 bolsas de cemento para "Estructura" y muestra confirmación con "Aprobación Pendiente"', async () => {
    iniciarSesionCapataz()
    const user = userEvent.setup()
    renderPagina()

    // Espera que termine la carga inicial de obras y catálogo
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Nueva requisición' }),
    ).toBeInTheDocument()

    // Selecciona Obra "Torre Alvear"
    const selectObra = screen.getByLabelText('Obra')
    await user.selectOptions(selectObra, OBRA_ID)

    // Selecciona Etapa "Estructura"
    const selectEtapa = screen.getByLabelText('Etapa de la obra')
    await user.selectOptions(selectEtapa, 'Estructura')

    // Selecciona Insumo "Cemento Portland Especial 50kg"
    const selectInsumo = screen.getByLabelText('Insumo #1')
    await user.selectOptions(selectInsumo, ARTICULO_CEMENTO_ID)

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
      screen.getByText(/80 bolsas — Cemento Portland Especial 50kg/),
    ).toBeInTheDocument()
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
    await user.type(screen.getByLabelText('Cantidad'), '80')
    await user.click(screen.getByRole('button', { name: 'Enviar requisición' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'No se pudo conectar con el servidor. Intentá nuevamente.',
    )
  })

  it('muestra mensaje de error y botón Reintentar si falla la carga inicial de obras o catálogo', async () => {
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

  it('muestra mensaje de error y botón Reintentar si falla aisladamente la carga del catálogo', async () => {
    iniciarSesionCapataz()
    // Obras responde normalmente (handler por defecto), pero falla el catálogo
    server.use(http.get('*/api/catalogo/insumos', () => HttpResponse.error()))

    const user = userEvent.setup()
    renderPagina()

    expect(
      await screen.findByText('No se pudieron cargar los datos necesarios.'),
    ).toBeInTheDocument()
    const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
    expect(botonReintentar).toBeInTheDocument()

    // Restablece el handler y reintenta
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

    // Selecciona Cemento en la primera fila
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)

    // En la segunda fila, la opción de Cemento no debe estar disponible para evitar duplicados
    const selectInsumo2 = screen.getByLabelText('Insumo #2')
    const opcionesInsumo2 = Array.from(selectInsumo2.querySelectorAll('option')).map(
      (opt) => opt.value,
    )
    expect(opcionesInsumo2).not.toContain(ARTICULO_CEMENTO_ID)

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
                descripcion: 'Cemento Portland Especial 50kg',
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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

describe('NuevaRequisicionPage - búsqueda de insumos', () => {
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

  it('al escribir en el buscador quedan solo los insumos que coinciden', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Buscar insumo'), 'arena')

    await waitFor(() =>
      expect(screen.queryByRole('option', { name: /Cemento/ })).not.toBeInTheDocument(),
    )
    expect(screen.getByRole('option', { name: /Arena Fina/ })).toBeInTheDocument()
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

    await user.type(screen.getByLabelText('Buscar insumo'), 'cal')

    await waitFor(() => expect(pedidos).toContain('cal'))
  })

  it('mantiene el insumo ya elegido en la fila aunque la búsqueda no lo traiga', async () => {
    const user = await abrirPagina()
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)

    await user.type(screen.getByLabelText('Buscar insumo'), 'arena')

    await waitFor(() =>
      expect(screen.queryByRole('option', { name: /Cal Hidráulica/ })).not.toBeInTheDocument(),
    )
    expect(screen.getByLabelText('Insumo #1')).toHaveValue(ARTICULO_CEMENTO_ID)
    expect(screen.getByText('Bolsas')).toBeInTheDocument()
  })

  it('al borrar la búsqueda vuelve el catálogo completo', async () => {
    const user = await abrirPagina()
    const buscador = screen.getByLabelText('Buscar insumo')
    await user.type(buscador, 'arena')
    await waitFor(() =>
      expect(screen.queryByRole('option', { name: /Cemento/ })).not.toBeInTheDocument(),
    )

    await user.clear(buscador)

    expect(await screen.findByRole('option', { name: /Cemento/ })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Arena Fina/ })).toBeInTheDocument()
  })

  it('apretar Enter en el buscador no envía el formulario', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Buscar insumo'), 'cal{Enter}')

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

    await user.type(screen.getByLabelText('Buscar insumo'), 'x')

    expect(
      await screen.findByText('No se pudo buscar insumos. Intentá de nuevo.'),
    ).toBeInTheDocument()
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
    const buscador = screen.getByLabelText('Buscar insumo')
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

    await user.type(screen.getByLabelText('Buscar insumo'), 'zzz')

    expect(
      await screen.findByText('No encontramos insumos con ese nombre'),
    ).toBeInTheDocument()
  })

  it('el botón Limpiar borra la búsqueda, devuelve el foco y vuelve el catálogo completo', async () => {
    const user = await abrirPagina()
    const buscador = screen.getByLabelText('Buscar insumo')
    expect(screen.queryByRole('button', { name: 'Limpiar búsqueda' })).not.toBeInTheDocument()

    await user.type(buscador, 'arena')
    await waitFor(() =>
      expect(screen.queryByRole('option', { name: /Cemento/ })).not.toBeInTheDocument(),
    )

    await user.click(screen.getByRole('button', { name: 'Limpiar búsqueda' }))

    expect(buscador).toHaveValue('')
    expect(buscador).toHaveFocus()
    expect(await screen.findByRole('option', { name: /Cemento/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Limpiar búsqueda' })).not.toBeInTheDocument()
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
              descripcion: 'Cemento Portland Especial 50kg',
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
    const buscador = screen.getByLabelText('Buscar insumo')

    await user.type(buscador, 'ce')
    await new Promise((r) => setTimeout(r, ESPERA_BUSQUEDA_MS + 100)) // sale la consulta lenta
    await user.clear(buscador)
    await user.type(buscador, 'ar')

    await waitFor(() =>
      expect(screen.queryByRole('option', { name: /Cemento/ })).not.toBeInTheDocument(),
    )
    expect(screen.getByRole('option', { name: /Arena Fina/ })).toBeInTheDocument()

    await new Promise((r) => setTimeout(r, 900)) // llega la respuesta vieja

    expect(screen.queryByRole('option', { name: /Cemento/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Arena Fina/ })).toBeInTheDocument()
  }, 15000)
})

describe('NuevaRequisicionPage - resultados a la vista', () => {
  const ARTICULO_ARENA_ID = 'c1c2c3c4-0000-0000-0000-000000000002'

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

  it('muestra los resultados debajo del buscador, con la cantidad encontrada', async () => {
    const user = await abrirPagina()

    await user.type(screen.getByLabelText('Buscar insumo'), 'cemento')

    expect(await screen.findByText(/1 insumo encontrado\./)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /MAT-CEM-001/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /MAT-ARE-001/ })).not.toBeInTheDocument()
  })

  it('al tocar un resultado lo pone en la primera fila sin insumo', async () => {
    const user = await abrirPagina()
    await user.type(screen.getByLabelText('Buscar insumo'), 'cemento')

    await user.click(await screen.findByRole('button', { name: /MAT-CEM-001/ }))

    expect(screen.getByLabelText('Insumo #1')).toHaveValue(ARTICULO_CEMENTO_ID)
    expect(screen.getByText('Bolsas')).toBeInTheDocument()
  })

  it('si todas las filas ya tienen insumo, agrega una fila nueva', async () => {
    const user = await abrirPagina()
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
    await user.type(screen.getByLabelText('Buscar insumo'), 'arena')

    await user.click(await screen.findByRole('button', { name: /MAT-ARE-001/ }))

    expect(screen.getByLabelText('Insumo #1')).toHaveValue(ARTICULO_CEMENTO_ID)
    expect(screen.getByLabelText('Insumo #2')).toHaveValue(ARTICULO_ARENA_ID)
  })

  it('un resultado que ya está agregado queda deshabilitado', async () => {
    const user = await abrirPagina()
    await user.type(screen.getByLabelText('Buscar insumo'), 'cemento')

    await user.click(await screen.findByRole('button', { name: /MAT-CEM-001/ }))

    expect(screen.getByRole('button', { name: /MAT-CEM-001.*ya agregado/ })).toBeDisabled()
  })
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
                descripcion: 'Cemento Portland Especial 50kg',
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
    await user.selectOptions(screen.getByLabelText('Insumo #1'), ARTICULO_CEMENTO_ID)
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
