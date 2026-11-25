import { apiFetch } from '../api/client'
import type { CotizacionResponseDto } from './licitacionesTipos'
import type { CotizarConMagicLinkRequestDto, PliegoMagicLinkDto } from './magicLinkTipos'

/**
 * Abre el pliego de un Magic Link SIN gastarlo (#158). Es público: no hace falta iniciar sesión.
 * Fuente: GET /api/licitaciones/magic-links/{token}
 * 404 si el enlace no existe, 409 si ya se usó, 400 si venció o la licitación ya no recibe ofertas.
 */
export function obtenerPliegoPorMagicLink(token: string): Promise<PliegoMagicLinkDto> {
  return apiFetch<PliegoMagicLinkDto>(`/api/licitaciones/magic-links/${encodeURIComponent(token)}`)
}

/**
 * Envía la cotización con un Magic Link. Al salir bien, el enlace queda usado y no se puede repetir.
 * Fuente: POST /api/licitaciones/magic-links/{token}/cotizaciones (público)
 * Si la cotización no se pudo guardar, el backend deja el enlace usable otra vez.
 */
export function cotizarConMagicLink(
  token: string,
  dto: CotizarConMagicLinkRequestDto,
): Promise<CotizacionResponseDto> {
  return apiFetch<CotizacionResponseDto>(`/api/licitaciones/magic-links/${encodeURIComponent(token)}/cotizaciones`, {
    method: 'POST',
    body: JSON.stringify(dto),
  })
}
