import { Link } from 'react-router'
import { InsigniaCantidad } from '../licitaciones/InsigniaCantidad'
import { RequisicionesPorLicitar } from '../licitaciones/RequisicionesPorLicitar'
import { useRequisicionesPorLicitar } from '../licitaciones/useRequisicionesPorLicitar'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'

export function ComprasPage() {
  const porLicitar = useRequisicionesPorLicitar()

  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>

      {/* Lo principal del Comprador: lo que espera su aprobación. */}
      <ResumenAprobacion />

      {/* Lo siguiente: las ya aprobadas que todavía no se publicaron como licitación. */}
      <RequisicionesPorLicitar porLicitar={porLicitar} />

      <ul className="lista-acciones">
        <li>
          <Link to="/compras/requisiciones/historial" className="accion-secundaria">
            <span>
              Historial de requisiciones
              <InsigniaCantidad
                cantidad={porLicitar.length}
                descripcion={
                  porLicitar.length === 1
                    ? '1 requisición aprobada espera su licitación'
                    : `${porLicitar.length} requisiciones aprobadas esperan su licitación`
                }
              />
            </span>
          </Link>
        </li>
        <li>
          <Link to="/compras/obras" className="accion-secundaria">
            Ver obras
          </Link>
        </li>
        <li>
          <Link to="/compras/licitaciones" className="accion-secundaria">
            Licitaciones y cotizaciones
          </Link>
        </li>
        <li>
          <Link to="/compras/ordenes" className="accion-secundaria">
            Órdenes de compra
          </Link>
        </li>
        <li>
          <Link to="/compras/presupuesto" className="accion-secundaria">
            Presupuesto
          </Link>
        </li>
        <li>
          <Link to="/compras/insumos/carga" className="accion-secundaria">
            Cargar insumos (CSV)
          </Link>
        </li>
        <li>
          <Link to="/" className="accion-secundaria">
            Volver al inicio
          </Link>
        </li>
      </ul>
    </section>
  )
}
