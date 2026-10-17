import { AuthProvider } from './auth/AuthContext'
import { useAuth } from './auth/useAuth'
import { InicioPage } from './pages/InicioPage'
import { LoginPage } from './pages/LoginPage'

function ContenidoApp() {
  const { usuario } = useAuth()

  if (!usuario) {
    return <LoginPage />
  }

  return <InicioPage />
}

function App() {
  return (
    <AuthProvider>
      <ContenidoApp />
    </AuthProvider>
  )
}

export default App

