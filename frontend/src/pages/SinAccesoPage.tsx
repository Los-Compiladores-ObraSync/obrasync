import { Link } from 'react-router'

export function SinAccesoPage() {
  return (
    <section>
      <h1>Sin acceso</h1>
      <div className="tarjeta-aviso" role="alert">
        <h2>No podés entrar acá</h2>
        <p>Tu rol no tiene permiso para ver esta sección.</p>
      </div>
      <ul className="lista-acciones">
        <li>
          <Link to="/" className="accion-grande">
            Volver al inicio
          </Link>
        </li>
      </ul>
    </section>
  )
}
