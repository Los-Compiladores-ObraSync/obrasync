import { Link } from 'react-router'

export function ComprasPage() {
  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p>Portal del Comprador. Acá van las pantallas de compras de los próximos sprints.</p>
      <p>
        <Link to="/compras/obras/nueva">Crear una obra nueva</Link>
      </p>
      <p>
        <Link to="/compras/requisiciones">Aprobar requisiciones</Link>
      </p>
    </section>
  )
}
