import type { To } from 'react-router'

const PARAMETROS_HISTORIAL = ['tipo', 'obraId', 'desde', 'hasta', 'pagina']

/** Construye el destino de retorno solo para los detalles abiertos desde el historial. */
export function destinoDeRegreso(params: URLSearchParams): To | '/' {
  if (params.get('desdeHistorial') !== '1') return '/'
  const filtros = new URLSearchParams()
  for (const nombre of PARAMETROS_HISTORIAL) {
    const valor = params.get(nombre)
    if (valor) filtros.set(nombre, valor)
  }
  return {
    pathname: '/compras/historial',
    search: filtros.size ? `?${filtros.toString()}` : '',
  }
}
