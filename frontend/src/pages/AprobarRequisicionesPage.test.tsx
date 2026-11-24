import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { CampanaConPendientes } from '../notificaciones/CampanaConPendientes'
import { PendientesProvider } from '../notificaciones/PendientesProvider'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { AprobarRequisicionesPage } from './AprobarRequisicionesPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <AprobarRequisicionesPage />
    </MemoryRouter>,
  )
}

describe('AprobarRequisicionesPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('lista las requisiciones pendientes con su obra', async () => {
    renderPagina()

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getAllByText(/Obra: Torre Alvear/)).toHaveLength(2)
  })

  it('aprueba una requisición y la saca de la lista', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'La requisición REQ-2026-0011 fue aprobada.',
    )
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
  })

  it('muestra el detalle del servidor ante un 409 y conserva la lista', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', () =>
        HttpResponse.json({ status: 409, detail: 'Ya fue aprobada.' }, { status: 409 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya fue aprobada.')
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('avisa que no hay permiso ante un 403', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', () =>
        HttpResponse.json({ status: 403 }, { status: 403 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No tenés permiso para aprobar requisiciones.',
    )
  })

  it('muestra un aviso si no hay pendientes', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina()

    expect(
      await screen.findByText('No hay requisiciones pendientes de aprobación.'),
    ).toBeInTheDocument()
  })

  it('muestra error y Reintentar si falla la carga', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.error()))
    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument()
  })

  it('un doble clic seguido aprueba la requisición una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', async ({ params }) => {
        llamadas++
        await delay(50)
        return HttpResponse.json({
          requisicionId: params.id,
          numeroRequisicion: 'REQ-2026-0011',
          estado: 'Aprobada',
        })
      }),
    )
    renderPagina()
    const boton = await screen.findByRole('button', { name: 'Aprobar REQ-2026-0011' })

    // Dos clics en el mismo instante: el botón todavía no se deshabilitó.
    act(() => {
      fireEvent.click(boton)
      fireEvent.click(boton)
    })

    expect(await screen.findByRole('status')).toHaveTextContent('fue aprobada')
    expect(llamadas).toBe(1)
  })

  it('no envía el rechazo si el motivo está vacío o solo contiene espacios', async () => {
    let llamado = false
    server.use(
      http.post('*/api/requisiciones/:id/rechazar', () => {
        llamado = true
        return HttpResponse.json({}, { status: 200 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))

    const botonConfirmar = screen.getByRole('button', { name: 'Confirmar rechazo' })
    expect(botonConfirmar).toBeInTheDocument()

    // Clic sin escribir motivo
    await user.click(botonConfirmar)
    expect(llamado).toBe(false)
    expect(await screen.findByText('El motivo de rechazo es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()

    // Escribir solo espacios
    const textarea = screen.getByRole('textbox', { name: /motivo del rechazo/i })
    await user.type(textarea, '   ')
    await user.click(botonConfirmar)
    expect(llamado).toBe(false)
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('permite cancelar el formulario de rechazo y restaurar los botones', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))

    expect(screen.getByRole('button', { name: 'Confirmar rechazo' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' })).toBeInTheDocument()
  })

  it('rechaza una requisición exitosamente, la saca de la lista y muestra el mensaje', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))

    const textarea = screen.getByRole('textbox', { name: /motivo del rechazo/i })
    await user.type(textarea, 'Material fuera de presupuesto')
    await user.click(screen.getByRole('button', { name: 'Confirmar rechazo' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'La requisición REQ-2026-0011 fue rechazada.',
    )
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
  })

  it('muestra mensaje de error ante un 404 al rechazar y recarga la lista', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/rechazar', () =>
        HttpResponse.json(
          { status: 404, detail: 'La requisición ya no existe. Recargá la lista.' },
          { status: 404 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))
    await user.type(
      screen.getByRole('textbox', { name: /motivo del rechazo/i }),
      'Ya no se necesita',
    )
    await user.click(screen.getByRole('button', { name: 'Confirmar rechazo' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'La requisición ya no existe. Recargá la lista.',
    )
  })

  it('muestra mensaje ante un 409 al rechazar y recarga la lista', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/rechazar', () =>
        HttpResponse.json(
          { status: 409, detail: 'Solo se pueden rechazar requisiciones pendientes.' },
          { status: 409 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))
    await user.type(
      screen.getByRole('textbox', { name: /motivo del rechazo/i }),
      'No corresponde',
    )
    await user.click(screen.getByRole('button', { name: 'Confirmar rechazo' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Solo se pueden rechazar requisiciones pendientes.',
    )
  })

  it('un doble clic seguido rechaza la requisición una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/requisiciones/:id/rechazar', async ({ params }) => {
        llamadas++
        await delay(50)
        return HttpResponse.json({
          requisicionId: params.id,
          numeroRequisicion: 'REQ-2026-0011',
          estado: 'Rechazada',
        })
      }),
    )
    renderPagina()
    const user = userEvent.setup()
    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))
    await user.type(
      screen.getByRole('textbox', { name: /motivo del rechazo/i }),
      'Duplicada por error',
    )

    const boton = screen.getByRole('button', { name: 'Confirmar rechazo' })

    // Dos clics en el mismo instante: el botón todavía no se deshabilitó.
    act(() => {
      fireEvent.click(boton)
      fireEvent.click(boton)
    })

    expect(await screen.findByRole('status')).toHaveTextContent('fue rechazada')
    expect(llamadas).toBe(1)
  })

  it('la campana baja el número al rechazar una requisición con PendientesProvider', async () => {
    const usuarioComprador: SesionUsuario = {
      id: 'a1b2c3d4-0000-0000-0000-000000000002',
      nombreCompleto: 'Marta Gómez',
      email: 'comprador@obrasync.com',
      rol: 'Comprador',
    }
    const user = userEvent.setup()
    render(
      <AuthContext.Provider value={{ usuario: usuarioComprador, login: () => {}, logout: () => {} }}>
        <MemoryRouter>
          <PendientesProvider>
            <CampanaConPendientes />
            <AprobarRequisicionesPage />
          </PendientesProvider>
        </MemoryRouter>
      </AuthContext.Provider>,
    )

    expect(await screen.findByRole('button', { name: 'Notificaciones: 2 sin leer' })).toBeInTheDocument()
    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))
    await user.type(screen.getByRole('textbox', { name: /motivo del rechazo/i }), 'No corresponde')
    await user.click(screen.getByRole('button', { name: 'Confirmar rechazo' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'La requisición REQ-2026-0011 fue rechazada.',
    )
    expect(await screen.findByRole('button', { name: 'Notificaciones: 1 sin leer' })).toBeInTheDocument()
  })
})

describe('AprobarRequisicionesPage - paginación (#172)', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  function pendiente(n: number) {
    return {
      requisicionId: `d1d2d3d4-0000-0000-0000-0000000002${n}`,
      numeroRequisicion: `REQ-2026-02${n}`,
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
      etapa: 'Estructura',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
      estado: 'Pendiente',
      observaciones: null,
      fechaCreacion: '2026-10-19T12:00:00Z',
      items: [],
    }
  }

  function tresPendientesDeADos() {
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina') ?? '1')
        const filas = pagina === 1 ? [pendiente(11), pendiente(12)] : [pendiente(13)]
        return HttpResponse.json(filas, { headers: { 'X-Total-Count': '3' } })
      }),
    )
  }

  it('si hay más pendientes que las cargadas, "Ver más" trae las que faltan', async () => {
    tresPendientesDeADos()
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByText('REQ-2026-0211')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0213')).not.toBeInTheDocument()
    expect(screen.getByText('Mostrando 2 de 3.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    expect(await screen.findByText('REQ-2026-0213')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ver más' })).not.toBeInTheDocument()
  })

  it('al aprobar una, el total baja y "Ver más" sigue trayendo lo que falta', async () => {
    tresPendientesDeADos()
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', ({ params }) =>
        HttpResponse.json({ ...pendiente(11), requisicionId: params.id, estado: 'Aprobada' }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()
    await screen.findByText('REQ-2026-0211')

    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0211' }))
    await screen.findByText('Mostrando 1 de 2.')

    expect(screen.queryByText('REQ-2026-0211')).not.toBeInTheDocument()
  })

  it('permite elegir un motivo frecuente de rechazo en vez de escribirlo', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Rechazar REQ-2026-0011' }))
    await user.click(screen.getByRole('button', { name: 'Cantidad mayor a la necesaria' }))

    expect(screen.getByRole('textbox', { name: /motivo del rechazo/i })).toHaveValue(
      'Cantidad mayor a la necesaria',
    )
    await user.click(screen.getByRole('button', { name: 'Confirmar rechazo' }))

    expect(await screen.findByRole('status')).toHaveTextContent('fue rechazada')
  })

  it('el detalle expandible muestra el total estimado con precios de referencia', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getAllByText('Ver detalle completo')[0])

    expect(screen.getAllByRole('columnheader', { name: 'Precio ref.' })[0]).toBeVisible()
  })

  it('al aprobar ofrece el paso siguiente: publicar la licitación de esa requisición', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    const enlace = await screen.findByRole('link', {
      name: /Publicar licitación de REQ-2026-0011/,
    })
    expect(enlace.getAttribute('href')).toMatch(/^\/compras\/requisiciones\/historial\/.+/)
  })
  // Contrato de requisiciones: solo trae solicitanteId; el nombre sale de GET /api/usuarios/capataces (#238).
  it('el detalle expandible muestra el nombre del solicitante', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getAllByText('Ver detalle completo')[0])

    expect(await screen.findAllByText(/Solicitante: Juan Pérez/)).not.toHaveLength(0)
  })

  it('si no se puede cargar la lista de capataces, el detalle se muestra sin solicitante', async () => {
    server.use(http.get('*/api/usuarios/capataces', () => HttpResponse.json({ title: 'Error' }, { status: 500 })))
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getAllByText('Ver detalle completo')[0])

    expect(screen.getAllByRole('columnheader', { name: 'Precio ref.' })[0]).toBeVisible()
    expect(screen.queryByText(/Solicitante:/)).not.toBeInTheDocument()
  })
})
