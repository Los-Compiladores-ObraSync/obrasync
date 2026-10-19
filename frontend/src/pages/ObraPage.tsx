import { Link } from 'react-router'

export function ObraPage() {
  return (
    <section>
      <h1>Obra / Terreno</h1>
      <p>Portal del Capataz. Acá van las pantallas de obra de los próximos sprints.</p>
      <p>
        <Link to="/obra/requisicion/nueva">+ Nueva Requisición</Link>
      </p>
    </section>
  )
}
