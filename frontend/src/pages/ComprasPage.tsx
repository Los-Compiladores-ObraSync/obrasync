import { Link } from 'react-router'

export function ComprasPage() {
  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>
      <ul className="lista-acciones">
        <li>
          <Link to="/compras/requisiciones" className="accion-grande">
            Aprobar requisiciones
          </Link>
        </li>
        <li>
          <Link to="/compras/obras/nueva" className="accion-grande">
            Crear una obra nueva
          </Link>
        </li>
      </ul>
    </section>
  )
}
