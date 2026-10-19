import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { setToken } from '../api/client'
import { STORAGE_KEY } from '../auth/sessionContext'
import { server } from '../mocks/server'
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
    expect(screen.getByText('BOLSA')).toBeInTheDocument()

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
      screen.getByText(/80 BOLSA — Cemento Portland Especial 50kg/),
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
