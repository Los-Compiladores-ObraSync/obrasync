import { Link } from 'react-router'

export function ObraPage() {
  return (
    <section>
      <h1>Obra / Terreno</h1>
      <p className="intro">Pedí los materiales que necesitás para tu obra.</p>
      <ul className="lista-acciones">
        <li>
          <Link to="/obra/requisicion/nueva" className="accion-grande">
            Nueva requisición
          </Link>
        </li>
      </ul>
    </section>
  )
}
