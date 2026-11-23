import { Navigate, Route, Routes, useNavigate } from 'react-router'
import { useEffect } from 'react'
import { AsistenteChatPage } from './asistente/AsistenteChatPage'
import { AuthProvider } from './auth/AuthContext'
import { LayoutApp } from './layout/LayoutApp'
import { RUTA_PUBLICA_MAGIC_LINK } from './licitaciones/magicLinkRutas'
import { AprobarRequisicionesPage } from './pages/AprobarRequisicionesPage'
import { CambiarContrasenaPage } from './pages/CambiarContrasenaPage'
import { CargaMasivaInsumosPage } from './pages/CargaMasivaInsumosPage'
import { ComprasPage } from './pages/ComprasPage'
import { CotizarConEnlacePage } from './pages/CotizarConEnlacePage'
import { CotizarLicitacionPage } from './pages/CotizarLicitacionPage'
import { DetalleRequisicionPage } from './pages/DetalleRequisicionPage'
import { HistorialRequisicionesPage } from './pages/HistorialRequisicionesPage'
import { HistorialComprasPage } from './pages/HistorialComprasPage'
import { SeguimientoPedidoPage } from './pages/SeguimientoPedidoPage'
import { InicioPage } from './pages/InicioPage'
import { LicitacionesComprasPage } from './pages/LicitacionesComprasPage'
import { OrdenesCompraPage } from './pages/OrdenesCompraPage'
import { LoginPage } from './pages/LoginPage'
import { EditarObraPage } from './pages/EditarObraPage'
import { NuevaObraPage } from './pages/NuevaObraPage'
import { NuevaRequisicionPage } from './pages/NuevaRequisicionPage'
import { ObraPage } from './pages/ObraPage'
import { ObrasPage } from './pages/ObrasPage'
import { PresupuestoPage } from './pages/PresupuestoPage'
import { MisCotizacionesPage } from './pages/MisCotizacionesPage'
import { MisOrdenesProveedorPage } from './pages/MisOrdenesProveedorPage'
import { MiReputacionProveedorPage } from './pages/MiReputacionProveedorPage'
import { ProveedoresPage } from './pages/ProveedoresPage'
import { RecepcionPage } from './pages/RecepcionPage'
import { RecuperarContrasenaPage } from './pages/RecuperarContrasenaPage'
import { RestablecerContrasenaPage } from './pages/RestablecerContrasenaPage'
import { RutaProtegida } from './routes/RutaProtegida'
import { SoloPortal } from './routes/SoloPortal'
import { SoloSinSesion } from './routes/SoloSinSesion'
import { FranjaModoSimulado } from './layout/FranjaModoSimulado'
import { useAuth } from './auth/useAuth'

/** /compras sigue siendo una dirección válida; para Comprador lleva al inicio ya unificado. */
function ComprasPortalIndex() {
  const { usuario } = useAuth()
  return usuario?.rol === 'Comprador' ? <Navigate to="/" replace /> : <ComprasPage />
}

function PushNavigationBridge() {
  const navigate = useNavigate()
  useEffect(() => {
    const worker = navigator.serviceWorker
    if (!worker) return
    const recibir = (evento: MessageEvent) => {
      if (evento.data?.type === 'OBRASYNC_PUSH_VISIBILITY_QUERY') {
        evento.ports[0]?.postMessage(document.visibilityState === 'visible' ? 'visible' : 'hidden')
      }
      if (evento.data?.type === 'OBRASYNC_PUSH_NAVIGATE' && typeof evento.data.ruta === 'string' && evento.data.ruta.startsWith('/') && !evento.data.ruta.startsWith('//')) {
        navigate(evento.data.ruta)
      }
    }
    worker.addEventListener('message', recibir)
    return () => worker.removeEventListener('message', recibir)
  }, [navigate])
  return null
}

// El Router (BrowserRouter) se pone en main.tsx; acá solo se declaran las rutas.
// Así las pruebas pueden envolver <App /> en un MemoryRouter y elegir la ruta inicial.
// Si se cambia un `path` de un portal, hay que cambiar también `ruta` en portales.ts
// (hay una prueba que avisa si dejan de coincidir).
function App() {
  return (
    <>
      <PushNavigationBridge />
      <FranjaModoSimulado />
      <AuthProvider>
        <Routes>
          {/* Pública y sin sesión: el proveedor cotiza desde su Magic Link (#235). No usa el menú ni el login. */}
          <Route path={`${RUTA_PUBLICA_MAGIC_LINK}/:token`} element={<CotizarConEnlacePage />} />

          {/* Pública: solo se ve sin sesión */}
          <Route element={<SoloSinSesion />}>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/recuperar-contrasena" element={<RecuperarContrasenaPage />} />
            <Route path="/restablecer-contrasena" element={<RestablecerContrasenaPage />} />
          </Route>

          {/* Protegidas: requieren sesión y comparten el encabezado con el menú */}
          <Route element={<RutaProtegida />}>
            <Route element={<LayoutApp />}>
              <Route index element={<InicioPage />} />
              {/* Cualquier rol con sesión puede cambiar su propia contraseña. */}
              <Route path="cambiar-contrasena" element={<CambiarContrasenaPage />} />

              {/* Un portal por rol. Cada portal puede sumar subrutas como hijas. */}
              <Route path="obra" element={<SoloPortal portalId="obra" />}>
                <Route index element={<ObraPage />} />
                <Route path="requisicion/nueva" element={<NuevaRequisicionPage />} />
                <Route path="recepcion" element={<RecepcionPage />} />
                <Route path="asistente" element={<AsistenteChatPage />} />
                {/* Misma pantalla que en Compras: el Capataz solo consulta (el formulario es de Comprador y Admin) */}
                <Route path="presupuesto" element={<PresupuestoPage />} />
                <Route
                  path="requisiciones"
                  element={
                    <HistorialRequisicionesPage volverA="/" textoVolver="← Volver al inicio" />
                  }
                />
                <Route
                  path="requisiciones/:id"
                  element={<DetalleRequisicionPage volverA="/obra/requisiciones" />}
                />
                <Route path="requisiciones/:id/seguimiento" element={<SeguimientoPedidoPage />} />
              </Route>
              <Route path="compras" element={<SoloPortal portalId="compras" />}>
                <Route index element={<ComprasPortalIndex />} />
                <Route path="obras" element={<ObrasPage />} />
                <Route path="obras/nueva" element={<NuevaObraPage />} />
                <Route path="obras/:id/editar" element={<EditarObraPage />} />
                <Route path="requisiciones" element={<AprobarRequisicionesPage />} />
                <Route path="asistente" element={<AsistenteChatPage portal="compras" />} />
                <Route path="licitaciones" element={<LicitacionesComprasPage />} />
                <Route path="ordenes" element={<OrdenesCompraPage />} />
                <Route path="recepcion" element={<RecepcionPage />} />
                <Route path="historial" element={<HistorialComprasPage />} />
                <Route
                  path="historial/requisiciones/:id"
                  element={
                    <DetalleRequisicionPage
                      volverA="/compras/historial"
                      rutaBaseSeguimiento="/compras/historial/requisiciones"
                    />
                  }
                />
                <Route
                  path="historial/requisiciones/:id/seguimiento"
                  element={<SeguimientoPedidoPage volverABase="/compras/historial/requisiciones" />}
                />
                <Route path="presupuesto" element={<PresupuestoPage />} />
                <Route path="insumos/carga" element={<CargaMasivaInsumosPage />} />
                <Route
                  path="requisiciones/historial"
                  element={
                    <HistorialRequisicionesPage
                      volverA="/"
                      textoVolver="← Volver al inicio"
                    />
                  }
                />
                <Route
                  path="requisiciones/historial/:id"
                  element={
                    <DetalleRequisicionPage
                      volverA="/compras/requisiciones/historial"
                      rutaBaseSeguimiento="/compras/requisiciones/historial"
                    />
                  }
                />
                <Route
                  path="requisiciones/historial/:id/seguimiento"
                  element={<SeguimientoPedidoPage volverABase="/compras/requisiciones/historial" />}
                />
              </Route>
              <Route path="proveedores" element={<SoloPortal portalId="proveedores" />}>
                <Route index element={<ProveedoresPage />} />
                <Route path="licitaciones/:id" element={<CotizarLicitacionPage />} />
                <Route path="mis-cotizaciones" element={<MisCotizacionesPage />} />
                <Route path="mis-ordenes" element={<MisOrdenesProveedorPage />} />
                <Route path="mi-reputacion" element={<MiReputacionProveedorPage />} />
              </Route>
            </Route>
          </Route>

          {/* Cualquier otra dirección: al inicio (y si no hay sesión, de ahí al login) */}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </>
  )
}

export default App
