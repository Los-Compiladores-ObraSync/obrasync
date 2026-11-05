import { Link } from 'react-router'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'

export function ComprasPage() {
  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>

      {/* Lo principal del Comprador: lo que espera su aprobación. */}
      <ResumenAprobacion />

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
      </ul>
    </section>
  )
}
