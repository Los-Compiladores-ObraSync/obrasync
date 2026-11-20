import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { MOCK_TOKEN_CAPATAZ, MOCK_TOKEN_COMPRADOR } from '../mocks/handlers'
import * as modoSimulado from '../mocks/modoSimulado'
import { server } from '../mocks/server'
import { CargaMasivaInsumosPage } from './CargaMasivaInsumosPage'

const ENCABEZADO = 'codigo;descripcion;unidadMedida;moneda;precioReferencia;activo'

function renderPagina() {
  return render(
    <MemoryRouter>
      <CargaMasivaInsumosPage />
    </MemoryRouter>,
  )
}

async function subir(user: ReturnType<typeof userEvent.setup>, contenido: string, nombre = 'insumos.csv') {
  const archivo = new File([contenido], nombre, { type: 'text/csv' })
  await user.upload(screen.getByLabelText('Archivo CSV'), archivo)
}

describe('CargaMasivaInsumosPage (#215)', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(MOCK_TOKEN_COMPRADOR)
    vi.restoreAllMocks()
    vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
  })

  it('ofrece descargar la plantilla con las columnas del contrato', () => {
    renderPagina()

    const enlace = screen.getByRole('link', { name: 'Descargar la plantilla' })
    expect(enlace).toHaveAttribute('download', 'plantilla-insumos.csv')
    expect(decodeURIComponent(enlace.getAttribute('href') ?? '')).toContain(
      'codigo;descripcion;unidadMedida;moneda;precioReferencia;activo',
    )
    expect(screen.getByRole('link', { name: '← Volver a Gestión y Compras' })).toHaveAttribute('href', '/compras')
  })

  it('carga las filas válidas y muestra el resumen', async () => {
    const user = userEvent.setup()
    renderPagina()

    await subir(user, `${ENCABEZADO}\nMAT-NUE-001;Ladrillo hueco;UNIDAD;ARS;250;si\nMAT-NUE-002;Hierro 8mm;KG;;;`)

    expect(await screen.findByText(/2 insumos listos para cargar/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cargar 2 insumos' }))

    expect(await screen.findByText('Se cargaron 2 de 2 insumos.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cargar otro archivo' })).toBeInTheDocument()
  })

  it('manda a cada fila los datos del CSV (precio en número, activo en booleano)', async () => {
    const cuerpos: unknown[] = []
    server.use(
      http.post('*/api/catalogo/insumos', async ({ request }) => {
        cuerpos.push(await request.json())
        return HttpResponse.json({ articuloId: 'x', codigo: 'MAT-NUE-001' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await subir(user, `${ENCABEZADO}\nMAT-NUE-001;Ladrillo hueco;UNIDAD;ARS;1.250,50;no`)
    await user.click(await screen.findByRole('button', { name: 'Cargar 1 insumo' }))
    await screen.findByText('Se cargaron 1 de 1 insumos.')

    expect(cuerpos).toEqual([
      {
        codigo: 'MAT-NUE-001',
        descripcion: 'Ladrillo hueco',
        unidadMedida: 'UNIDAD',
        moneda: 'ARS',
        precioReferencia: 1250.5,
        activo: false,
      },
    ])
  })

  it('las filas con error de formato se informan y NO se envían; las válidas sí', async () => {
    let pedidos = 0
    server.use(
      http.post('*/api/catalogo/insumos', () => {
        pedidos += 1
        return HttpResponse.json({ articuloId: 'x' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await subir(user, `${ENCABEZADO}\n;Sin código;KG;;;\nMAT-NUE-002;Hierro 8mm;KG;;;`)

    expect(await screen.findByText(/1 insumo listo para cargar/)).toBeInTheDocument()
    expect(screen.getByText(/Fila 2: Falta el código/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cargar 1 insumo' }))
    await screen.findByText('Se cargaron 1 de 1 insumos.')
    expect(pedidos).toBe(1)
    expect(screen.getByText(/sin enviar por errores de formato/)).toBeInTheDocument()
  })

  it('un código que ya existe (409) se anota con su motivo y no frena al resto', async () => {
    const user = userEvent.setup()
    renderPagina()

    // MAT-CEM-001 ya está en el catálogo simulado.
    await subir(user, `${ENCABEZADO}\nMAT-CEM-001;Cemento repetido;BOLSA;;;\nMAT-NUE-003;Pintura;LITRO;;;`)
    await user.click(await screen.findByRole('button', { name: 'Cargar 2 insumos' }))

    expect(await screen.findByText('Se cargaron 1 de 2 insumos.')).toBeInTheDocument()
    expect(screen.getByText(/Fila 2 \(MAT-CEM-001\): Ya existe un insumo con el código MAT-CEM-001\./)).toBeInTheDocument()
  })

  it('si falta una columna obligatoria avisa y no ofrece cargar', async () => {
    const user = userEvent.setup()
    renderPagina()

    await subir(user, 'codigo;descripcion\nMAT-1;Cemento')

    expect(await screen.findByRole('alert')).toHaveTextContent('Faltan columnas obligatorias: unidadMedida')
    expect(screen.queryByRole('button', { name: /^Cargar \d/ })).not.toBeInTheDocument()
  })

  it('si el rol no tiene permiso (403) corta la carga y avisa cuántas filas no se enviaron', async () => {
    setToken(MOCK_TOKEN_CAPATAZ)
    const user = userEvent.setup()
    renderPagina()

    await subir(user, `${ENCABEZADO}\nMAT-NUE-004;Uno;KG;;;\nMAT-NUE-005;Dos;KG;;;`)
    await user.click(await screen.findByRole('button', { name: 'Cargar 2 insumos' }))

    expect(await screen.findByText('Se cargaron 0 de 2 insumos.')).toBeInTheDocument()
    const alerta = screen.getByRole('alert')
    expect(alerta).toHaveTextContent('Tu rol no tiene permiso para cargar insumos.')
    expect(alerta).toHaveTextContent('No se enviaron 2 filas.')
  })

  it('si se corta la conexión frena en la primera fila y deja reintentar con otro archivo', async () => {
    let pedidos = 0
    server.use(
      http.post('*/api/catalogo/insumos', () => {
        pedidos += 1
        return HttpResponse.error()
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await subir(user, `${ENCABEZADO}\nMAT-NUE-006;Uno;KG;;;\nMAT-NUE-007;Dos;KG;;;\nMAT-NUE-008;Tres;KG;;;`)
    await user.click(await screen.findByRole('button', { name: 'Cargar 3 insumos' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Se cortó la conexión')
    expect(screen.getByRole('alert')).toHaveTextContent('No se enviaron 3 filas.')
    expect(pedidos).toBe(1)

    await user.click(screen.getByRole('button', { name: 'Cargar otro archivo' }))
    await waitFor(() => expect(screen.getByLabelText('Archivo CSV')).toBeInTheDocument())
  })
})
