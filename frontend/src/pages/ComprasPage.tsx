import { Link } from 'react-router'
import { RequisicionesPorLicitar } from '../licitaciones/RequisicionesPorLicitar'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'

export function ComprasPage() {
  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>

      {/* Lo principal del Comprador: lo que espera su aprobación. */}
      <ResumenAprobacion />

      {/* Lo siguiente: las ya aprobadas que todavía no se publicaron como licitación. */}
      <RequisicionesPorLicitar />

      <ul className="lista-acciones">
        <li>
          <Link to="/compras/requisiciones/historial" className="accion-secundaria">
            Historial de requisiciones
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
