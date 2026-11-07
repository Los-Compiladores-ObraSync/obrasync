import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { ObraResumen } from '../requisiciones/requisicionesTipos'
import { NuevaObraPage } from './NuevaObraPage'
import styles from './ObrasPage.module.css'

/**
 * Pantalla /compras/obras/:id/editar (#119). No hay un endpoint "obtener una obra", así que se
 * busca en la lista (GET /api/obras) y, con los datos, se muestra el mismo formulario de la
 * pantalla "Nueva obra" en modo edición.
 */
export function EditarObraPage() {
  const { id } = useParams()
  const [obra, setObra] = useState<ObraResumen | null>(null)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    obtenerObras()
      .then((lista) => {
        if (cancelado) return
        const encontrada = lista.find((o) => o.id === id)
        if (encontrada) setObra(encontrada)
        else setError('La obra no existe o fue dada de baja.')
      })
      .catch(() => {
        if (!cancelado) setError('No se pudo cargar la obra. Intentá nuevamente.')
      })
      .finally(() => {
        if (!cancelado) setCargando(false)
      })
    return () => {
      cancelado = true
    }
  }, [id])

  if (cargando) return <p role="status">Cargando…</p>

  if (error || !obra) {
    return (
      <section className={`${styles.contenedor} pantalla-ancha`}>
        <Link to="/compras/obras" className={styles.volver}>
          ← Volver a obras
        </Link>
        <p className={styles.errorApi} role="alert">
          {error}
        </p>
      </section>
    )
  }

  return (
    <NuevaObraPage
      obra={{
        id: obra.id,
        nombre: obra.nombre,
        ubicacion: obra.ubicacion,
        fechaInicio: obra.fechaInicio,
        capatazId: obra.capatazId,
      }}
    />
  )
}
