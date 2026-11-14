import { estaEnModoSimulado } from '../mocks/modoSimulado'
import styles from './FranjaModoSimulado.module.css'

export function FranjaModoSimulado() {
    if (!estaEnModoSimulado()) return null
    return (
        <div role= "status" className = { styles.franja } >
            MODO SIMULADO: los datos no son reales
                </div>
  )
}