/** Cada cuánto la campana vuelve a preguntarle al backend por sus avisos (#48, #219: 30 segundos).
 *  Lo usan todos los proveedores de avisos. Está en un solo lugar para cambiarlo fácil. */
export const INTERVALO_REFRESCO_MS = 30_000

/** Roles que pueden aprobar requisiciones (POST /aprobar), y por eso son los
 *  únicos que ven el número en la campana. */
export const ROLES_QUE_VEN_PENDIENTES: readonly string[] = ['Comprador', 'Admin']

/** Desde cuántas requisiciones pendientes la campana se resalta (color, animación
 *  y texto para lector de pantalla). Valor propuesto en #50; se cambia solo acá. */
export const UMBRAL_PENDIENTES = 5

/** Cuántas requisiciones pendientes (las más antiguas) lista el panel de la campana, para
 *  elegir una y ir directo a ella. Pocas, para que el panel entre en la pantalla del celular.
 *  El resto está en Aprobar requisiciones. */
export const MAX_PENDIENTES_EN_CAMPANA = 3

/** Cuántas novedades (requisiciones aprobadas o rechazadas del Capataz, licitaciones nuevas del
 *  Proveedor) lista el panel de la campana. Pocas, para que entre en el celular; el resto se ve
 *  al tocar "Marcar como vistas" o entrando a la pantalla correspondiente (#163). */
export const MAX_NOVEDADES_EN_CAMPANA = 3

/** Cuántos renglones (una orden con varios avisos cuenta como uno) lista cada sección nueva del
 *  panel ("Para hacer" y "Novedades") con los avisos del flujo de órdenes (#219). Pocos, para que
 *  entre en el celular. */
export const MAX_AVISOS_EN_CAMPANA = 4

/** Una licitación abierta se avisa como "por vencer" cuando falta esta cantidad de horas o menos (#219). */
export const HORAS_LICITACION_POR_VENCER = 24

/** Para avisar cotizaciones nuevas se consulta cada licitación abierta; para no hacer decenas de pedidos
 *  cada 30 segundos se miran solo las más próximas a vencer. Mejora posible: que Licitaciones informe la
 *  cantidad de cotizaciones en GET /api/licitaciones (tarjeta para ese squad). */
export const MAX_LICITACIONES_CONSULTADAS_PARA_COTIZACIONES = 10

/** Al abrir la campana se actualizan los avisos, salvo que ya se hayan consultado hace menos de esto
 *  (abrir y cerrar el panel seguido no tiene que disparar todos los pedidos cada vez). */
export const ESPERA_MINIMA_AL_ABRIR_CAMPANA_MS = 5_000
