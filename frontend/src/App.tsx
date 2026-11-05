import { Navigate, Route, Routes } from 'react-router'
import { AuthProvider } from './auth/AuthContext'
import { LayoutApp } from './layout/LayoutApp'
import { AprobarRequisicionesPage } from './pages/AprobarRequisicionesPage'
import { CambiarContrasenaPage } from './pages/CambiarContrasenaPage'
import { ComprasPage } from './pages/ComprasPage'
import { CotizarLicitacionPage } from './pages/CotizarLicitacionPage'
import { DetalleRequisicionPage } from './pages/DetalleRequisicionPage'
import { HistorialRequisicionesPage } from './pages/HistorialRequisicionesPage'
import { InicioPage } from './pages/InicioPage'
import { LicitacionesComprasPage } from './pages/LicitacionesComprasPage'
import { LoginPage } from './pages/LoginPage'
import { NuevaObraPage } from './pages/NuevaObraPage'
import { NuevaRequisicionPage } from './pages/NuevaRequisicionPage'
import { ObraPage } from './pages/ObraPage'
import { ObrasPage } from './pages/ObrasPage'
import { ProveedoresPage } from './pages/ProveedoresPage'
import { RecepcionPage } from './pages/RecepcionPage'
import { RecuperarContrasenaPage } from './pages/RecuperarContrasenaPage'
import { RestablecerContrasenaPage } from './pages/RestablecerContrasenaPage'
import { RutaProtegida } from './routes/RutaProtegida'
import { SoloPortal } from './routes/SoloPortal'
import { SoloSinSesion } from './routes/SoloSinSesion'

// El Router (BrowserRouter) se pone en main.tsx; acá solo se declaran las rutas.
// Así las pruebas pueden envolver <App /> en un MemoryRouter y elegir la ruta inicial.
// Si se cambia un `path` de un portal, hay que cambiar también `ruta` en portales.ts
// (hay una prueba que avisa si dejan de coincidir).
function App() {
  return (
    <AuthProvider>
      <Routes>
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
            </Route>
            <Route path="compras" element={<SoloPortal portalId="compras" />}>
              <Route index element={<ComprasPage />} />
              <Route path="obras" element={<ObrasPage />} />
              <Route path="obras/nueva" element={<NuevaObraPage />} />
              <Route path="requisiciones" element={<AprobarRequisicionesPage />} />
              <Route path="licitaciones" element={<LicitacionesComprasPage />} />
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
                element={<DetalleRequisicionPage volverA="/compras/requisiciones/historial" />}
              />
            </Route>
            <Route path="proveedores" element={<SoloPortal portalId="proveedores" />}>
              <Route index element={<ProveedoresPage />} />
              <Route path="licitaciones/:id" element={<CotizarLicitacionPage />} />
            </Route>
          </Route>
        </Route>

        {/* Cualquier otra dirección: al inicio (y si no hay sesión, de ahí al login) */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  )
}

export default App
