import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { setToken } from '../api/client'
import { server } from '../mocks/server'
import { NuevaObraPage } from './NuevaObraPage'

const URL_OBRAS = 'http://localhost:5000/api/obras'
const URL_CAPATACES = 'http://localhost:5000/api/usuarios/capataces'
const CAPATAZ_ID = 'a1b2c3d4-0000-0000-0000-000000000001'
const URL_PLANTILLAS = 'http://localhost:5000/api/plantillas-obra'
const URL_NOMINATIM = 'https://nominatim.openstreetmap.org/search'

function renderPagina() {
  return render(
    <MemoryRouter>
      <NuevaObraPage />
    </MemoryRouter>,
  )
}

async function completarFormulario(
  user: ReturnType<typeof userEvent.setup>,
  nombre = 'Torre Norte',
) {
  await user.type(screen.getByLabelText('Nombre de la obra'), nombre)
  await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
  await user.type(screen.getByLabelText('Fecha de inicio'), '2026-11-02')
  await user.selectOptions(await screen.findByLabelText('Capataz'), CAPATAZ_ID)
  await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
}

describe('NuevaObraPage - validar la ubicación con el mapa (#113)', () => {
  async function completarSinUbicacion(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText('Nombre de la obra'), 'Torre Norte')
    await user.type(screen.getByLabelText('Fecha de inicio'), '2026-11-02')
    await user.selectOptions(await screen.findByLabelText('Capataz'), CAPATAZ_ID)
    await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
  }

  function contarObras() {
    const estado = { creadas: 0 }
    server.use(
      http.post(URL_OBRAS, () => {
        estado.creadas++
        return HttpResponse.text('id', { status: 200 })
      }),
    )
    return estado
  }

  it('al salir del campo avisa que la dirección existe', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.tab()

    expect(await screen.findByText(/Dirección encontrada/)).toBeInTheDocument()
  })

  it('si no la encuentra avisa y no crea la obra hasta confirmarlo', async () => {
    const obras = contarObras()
    const user = userEvent.setup()
    renderPagina()
    await completarSinUbicacion(user)

    await user.type(screen.getByLabelText('Ubicación'), 'Calle Inexistente 123')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText(/No encontramos esa dirección/)).toBeInTheDocument()
    expect(obras.creadas).toBe(0)

    await user.click(screen.getByLabelText('Es correcta, quiero crear la obra con esta ubicación'))
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(obras.creadas).toBe(1)
  })

  it('editar la ubicación borra la confirmación anterior', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Ubicación'), 'Calle Inexistente 123')
    await user.tab()
    const casilla = await screen.findByLabelText('Es correcta, quiero crear la obra con esta ubicación')
    await user.click(casilla)
    expect(casilla).toBeChecked()

    await user.type(screen.getByLabelText('Ubicación'), '4')

    expect(screen.queryByText(/No encontramos esa dirección/)).not.toBeInTheDocument()
  })

  it('si el servicio de mapas está caído, avisa y la obra se crea igual', async () => {
    server.use(http.get(URL_NOMINATIM, () => HttpResponse.error()))
    const obras = contarObras()
    const user = userEvent.setup()
    renderPagina()
    await completarSinUbicacion(user)

    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(obras.creadas).toBe(1)
  })

  it('un 429 del servicio (límite de uso) tampoco frena la creación', async () => {
    server.use(http.get(URL_NOMINATIM, () => new HttpResponse(null, { status: 429 })))
    const obras = contarObras()
    const user = userEvent.setup()
    renderPagina()
    await completarSinUbicacion(user)

    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.tab()
    expect(await screen.findByText(/No se pudo comprobar la dirección/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(obras.creadas).toBe(1)
  })

  it('no repite la consulta: salir del campo y enviar es un solo pedido', async () => {
    let consultas = 0
    server.use(
      http.get(URL_NOMINATIM, () => {
        consultas++
        return HttpResponse.json([{ place_id: 1 }])
      }),
    )
    contarObras()
    const user = userEvent.setup()
    renderPagina()
    await completarSinUbicacion(user)

    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.tab()
    await screen.findByText(/Dirección encontrada/)
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(consultas).toBe(1)
  })

  it('no manda el token ni el X-Correlation-ID al servicio externo', async () => {
    let cabeceras: Headers | undefined
    server.use(
      http.get(URL_NOMINATIM, ({ request }) => {
        cabeceras = request.headers
        return HttpResponse.json([{ place_id: 1 }])
      }),
    )
    setToken('token-secreto')
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.tab()
    await screen.findByText(/Dirección encontrada/)
    setToken(null)

    expect(cabeceras?.get('authorization')).toBeNull()
    expect(cabeceras?.get('x-correlation-id')).toBeNull()
  })

  it('con la ubicación vacía no consulta el mapa', async () => {
    let consultas = 0
    server.use(
      http.get(URL_NOMINATIM, () => {
        consultas++
        return HttpResponse.json([])
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByLabelText('Ubicación'))
    await user.tab()

    expect(consultas).toBe(0)
  })
})

describe('NuevaObraPage - plantillas de obra (#115)', () => {
  const ETAPAS_VIVIENDA = [
    'Replanteo y Nivelación',
    'Fundaciones',
    'Muros y Cubierta',
    'Instalaciones y Yesería',
    'Terminaciones',
  ]

  function valoresDeEtapas() {
    return screen
      .getAllByLabelText(/^Etapa \d+$/)
      .map((campo) => (campo as HTMLInputElement).value)
  }

  it('lista las plantillas y arranca en "Sin plantilla"', async () => {
    renderPagina()

    expect(await screen.findByRole('option', { name: 'Vivienda Unifamiliar' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Sin plantilla' })).toBeInTheDocument()
    expect(screen.getByLabelText('Empezar desde una plantilla (opcional)')).toHaveValue('')
    expect(valoresDeEtapas()).toEqual([''])
  })

  it('al elegir una plantilla completa las etapas en orden', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('option', { name: 'Vivienda Unifamiliar' })
    await user.selectOptions(
      screen.getByLabelText('Empezar desde una plantilla (opcional)'),
      screen.getByRole('option', { name: 'Vivienda Unifamiliar' }),
    )

    expect(valoresDeEtapas()).toEqual(ETAPAS_VIVIENDA)
    expect(screen.getByText(/casas residenciales/)).toBeInTheDocument()
  })

  it('las etapas de la plantilla se pueden editar, agregar y quitar', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByRole('option', { name: 'Vivienda Unifamiliar' })
    await user.selectOptions(
      screen.getByLabelText('Empezar desde una plantilla (opcional)'),
      screen.getByRole('option', { name: 'Vivienda Unifamiliar' }),
    )
    await user.clear(screen.getByLabelText('Etapa 2'))
    await user.type(screen.getByLabelText('Etapa 2'), 'Cimientos')
    await user.click(screen.getByRole('button', { name: 'Quitar etapa 1' }))
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))

    expect(valoresDeEtapas()).toEqual([
      'Cimientos',
      'Muros y Cubierta',
      'Instalaciones y Yesería',
      'Terminaciones',
      '',
    ])
  })

  it('"Sin plantilla" deja el formulario como al principio', async () => {
    const user = userEvent.setup()
    renderPagina()

    const selector = screen.getByLabelText('Empezar desde una plantilla (opcional)')
    await screen.findByRole('option', { name: 'Nave Industrial' })
    await user.selectOptions(selector, screen.getByRole('option', { name: 'Nave Industrial' }))
    await user.selectOptions(selector, screen.getByRole('option', { name: 'Sin plantilla' }))

    expect(valoresDeEtapas()).toEqual([''])
  })

  it('crea la obra mandando las etapas de la plantilla en fasesEtapas', async () => {
    let cuerpo: { fasesEtapas?: string[] } | undefined
    server.use(
      http.post(URL_OBRAS, async ({ request }) => {
        cuerpo = (await request.json()) as { fasesEtapas?: string[] }
        return HttpResponse.text('id', { status: 200 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Nombre de la obra'), 'Casa Lomas')
    await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
    await user.type(screen.getByLabelText('Fecha de inicio'), '2026-11-02')
    await user.selectOptions(await screen.findByLabelText('Capataz'), CAPATAZ_ID)
    await screen.findByRole('option', { name: 'Vivienda Unifamiliar' })
    await user.selectOptions(
      screen.getByLabelText('Empezar desde una plantilla (opcional)'),
      screen.getByRole('option', { name: 'Vivienda Unifamiliar' }),
    )
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(cuerpo?.fasesEtapas).toEqual(ETAPAS_VIVIENDA)
    // Después de crear, el formulario vuelve a "Sin plantilla" y a una etapa vacía.
    expect(screen.getByLabelText('Empezar desde una plantilla (opcional)')).toHaveValue('')
    expect(valoresDeEtapas()).toEqual([''])
  })

  it('si falla la carga avisa, deja reintentar y la obra se puede crear igual', async () => {
    server.use(http.get(URL_PLANTILLAS, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    expect(
      await screen.findByText('No se pudo cargar la lista de plantillas. Podés crear la obra igual.'),
    ).toBeInTheDocument()
    // Se puede escribir la etapa a mano como siempre.
    await user.type(screen.getByLabelText('Nombre de la obra'), 'Torre Norte')
    await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
    expect(screen.getByLabelText('Etapa 1')).toHaveValue('Cimientos')

    server.resetHandlers()
    await user.click(screen.getByRole('button', { name: 'Reintentar plantillas' }))

    expect(await screen.findByRole('option', { name: 'Nave Industrial' })).toBeInTheDocument()
  })
})

describe('NuevaObraPage - crear capataz (#111)', () => {
  async function abrirFormularioCapataz(user: ReturnType<typeof userEvent.setup>) {
    await screen.findByRole('option', { name: 'Juan Pérez' })
    await user.click(screen.getByRole('button', { name: '+ Crear capataz' }))
  }

  it('crea el capataz, refresca la lista y lo deja seleccionado', async () => {
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Nombre completo del capataz'), 'Ana López')
    await user.type(screen.getByLabelText('Email del capataz'), 'ana@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña inicial'), 'secreta1')
    await user.click(screen.getByRole('button', { name: 'Guardar capataz' }))

    expect(await screen.findByText('Capataz Ana López creado y seleccionado.')).toBeInTheDocument()
    const opcion = await screen.findByRole('option', { name: 'Ana López' })
    expect(screen.getByLabelText('Capataz')).toHaveValue((opcion as HTMLOptionElement).value)
    expect(screen.queryByLabelText('Contraseña inicial')).not.toBeInTheDocument()
  })

  it('manda el rol Capataz fijo y no la contraseña en otro lado', async () => {
    let cuerpo: Record<string, string> | undefined
    server.use(
      http.post('http://localhost:5000/api/usuarios/registro', async ({ request }) => {
        cuerpo = (await request.json()) as Record<string, string>
        return HttpResponse.json(
          { id: 'x1', nombreCompleto: 'Ana López', email: 'a@b.com', token: 't', roles: ['Capataz'] },
          { status: 201 },
        )
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Nombre completo del capataz'), '  Ana López ')
    await user.type(screen.getByLabelText('Email del capataz'), 'a@b.com')
    await user.type(screen.getByLabelText('Contraseña inicial'), 'secreta1')
    await user.click(screen.getByRole('button', { name: 'Guardar capataz' }))

    await screen.findByText('Capataz Ana López creado y seleccionado.')
    expect(cuerpo).toEqual({
      nombreCompleto: 'Ana López',
      email: 'a@b.com',
      password: 'secreta1',
      rol: 'Capataz',
    })
  })

  it('valida los campos antes de enviar', async () => {
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Email del capataz'), 'ana@')
    await user.type(screen.getByLabelText('Contraseña inicial'), '123')
    await user.click(screen.getByRole('button', { name: 'Guardar capataz' }))

    expect(screen.getByText('El nombre completo es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('Ingresá un email válido.')).toBeInTheDocument()
    expect(screen.getByText(/al menos 6 caracteres/)).toBeInTheDocument()
  })

  it('avisa con claridad si el email ya existe', async () => {
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Nombre completo del capataz'), 'Otro Juan')
    await user.type(screen.getByLabelText('Email del capataz'), 'capataz@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña inicial'), 'secreta1')
    await user.click(screen.getByRole('button', { name: 'Guardar capataz' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe un usuario registrado')
  })

  it('Enter en el formulario del capataz no envía la obra', async () => {
    let obrasEnviadas = 0
    server.use(
      http.post(URL_OBRAS, () => {
        obrasEnviadas++
        return HttpResponse.text('id', { status: 200 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Nombre completo del capataz'), 'Ana López{Enter}')

    expect(obrasEnviadas).toBe(0)
  })

  it('protege contra el doble clic: un solo pedido', async () => {
    let pedidos = 0
    server.use(
      http.post('http://localhost:5000/api/usuarios/registro', async () => {
        pedidos++
        await delay(50)
        return HttpResponse.json(
          { id: 'x1', nombreCompleto: 'Ana López', email: 'a@b.com', token: 't', roles: ['Capataz'] },
          { status: 201 },
        )
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.type(screen.getByLabelText('Nombre completo del capataz'), 'Ana López')
    await user.type(screen.getByLabelText('Email del capataz'), 'a@b.com')
    await user.type(screen.getByLabelText('Contraseña inicial'), 'secreta1')
    const boton = screen.getByRole('button', { name: 'Guardar capataz' })
    await user.dblClick(boton)

    await screen.findByText('Capataz Ana López creado y seleccionado.')
    expect(pedidos).toBe(1)
  })

  it('se puede cancelar y volver a abrir', async () => {
    const user = userEvent.setup()
    renderPagina()
    await abrirFormularioCapataz(user)

    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByLabelText('Contraseña inicial')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Crear capataz' })).toBeInTheDocument()
  })
})

describe('NuevaObraPage', () => {
  it('carga los capataces en un desplegable', async () => {
    renderPagina()

    expect(await screen.findByRole('option', { name: 'Juan Pérez' })).toBeInTheDocument()
  })

  it('avisa y deja reintentar si falla la carga de capataces', async () => {
    server.use(http.get(URL_CAPATACES, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByText('No se pudo cargar la lista de capataces.')).toBeInTheDocument()

    server.resetHandlers()
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('option', { name: 'Juan Pérez' })).toBeInTheDocument()
  })

  it('avisa (sin bloquear) cuando la fecha de inicio ya pasó', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Fecha de inicio'), '2020-01-01')

    expect(
      screen.getByText('Esta fecha ya pasó. ¿La obra ya empezó? Si es así, está bien.'),
    ).toBeInTheDocument()
  })

  it('no avisa cuando la fecha de inicio es futura', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Fecha de inicio'), '2999-12-31')

    expect(screen.queryByText(/Esta fecha ya pasó/)).not.toBeInTheDocument()
  })

  it('arranca con una etapa vacía', () => {
    renderPagina()

    expect(screen.getByLabelText('Etapa 1')).toBeInTheDocument()
    expect(screen.queryByLabelText('Etapa 2')).not.toBeInTheDocument()
  })

  it('agrega y quita etapas', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    expect(screen.getByLabelText('Etapa 2')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Quitar etapa 2' }))
    expect(screen.queryByLabelText('Etapa 2')).not.toBeInTheDocument()
  })

  it('al quitar una etapa del medio conserva el texto de las otras', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 2'), 'Estructura')
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 3'), 'Techos')

    await user.click(screen.getByRole('button', { name: 'Quitar etapa 2' }))

    expect(screen.getByLabelText('Etapa 1')).toHaveValue('Cimientos')
    expect(screen.getByLabelText('Etapa 2')).toHaveValue('Techos')
  })

  it('muestra errores y no llama a la API si el formulario está vacío', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, () => {
        llamadas++
        return HttpResponse.text('x')
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(screen.getByText('El nombre es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La ubicación es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('La fecha de inicio es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('El capataz es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El nombre de la etapa es obligatorio.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('envía el cuerpo del contrato y avisa que se creó', async () => {
    let cuerpo: unknown = null
    server.use(
      http.post(URL_OBRAS, async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 2'), 'Estructura')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(cuerpo).toEqual({
      nombre: 'Torre Norte',
      ubicacion: 'Av. Siempre Viva 742',
      fechaInicio: '2026-11-02T00:00:00.000Z',
      capatazId: CAPATAZ_ID,
      fasesEtapas: ['Cimientos', 'Estructura'],
    })
    // El formulario queda limpio para cargar otra obra.
    expect(screen.getByLabelText('Nombre de la obra')).toHaveValue('')
    expect(screen.getByLabelText('Etapa 1')).toHaveValue('')
  })

  it('muestra el detalle del servidor en un 409 y conserva lo escrito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user, 'Obra Duplicada')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe una obra con ese nombre.')
    expect(screen.getByLabelText('Nombre de la obra')).toHaveValue('Obra Duplicada')
  })

  it('avisa que no hay permiso en un 403', async () => {
    server.use(
      http.post(URL_OBRAS, () => HttpResponse.json({ status: 403 }, { status: 403 })),
    )
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para crear obras.')
  })

  it('muestra un mensaje general si falla la red', async () => {
    server.use(http.post(URL_OBRAS, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar')
  })

  it('un doble envío seguido crea la obra una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, async () => {
        llamadas++
        await delay(50)
        return HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarFormulario(user)

    // Dos envíos en el mismo instante (doble clic, o Enter + clic): el botón todavía no se deshabilitó.
    const formulario = screen.getByRole('button', { name: 'Crear obra' }).closest('form')!
    act(() => {
      fireEvent.submit(formulario)
      fireEvent.submit(formulario)
    })

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })

  it('después de un error se puede volver a enviar', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, () => {
        llamadas++
        return llamadas === 1
          ? HttpResponse.json({ status: 500 }, { status: 500 })
          : HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarFormulario(user)

    await user.click(screen.getByRole('button', { name: 'Crear obra' }))
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(llamadas).toBe(2)
  })
})
