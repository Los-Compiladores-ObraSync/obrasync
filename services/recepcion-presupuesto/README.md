# Recepción y Presupuesto

## Cómo ejecutar el servicio

1. Tener SQL Server funcionando en el puerto 1433.
2. Abrir `ObraSync.Recepcion.Api` en Visual Studio.
3. Configurar la conexión a la base de datos.
4. Ejecutar con F5 usando el perfil `http`.

El servicio funciona en `http://localhost:5004` y Swagger se puede abrir en `http://localhost:5004/swagger`.

## Configurar la contraseña

La conexión a `DB_Recepcion` está en `appsettings.json`, pero no incluye la contraseña para evitar subirla a GitHub.

Cada integrante debe configurar la contraseña de su SQL Server:

1. Abrir una terminal en la carpeta `ObraSync.Recepcion.Api`.
2. Ejecutar una sola vez:

    dotnet user-secrets init

3. Configurar la conexión:

    dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=DB_Recepcion;User Id=sa;Password=TU_PASSWORD;TrustServerCertificate=True;"

Reemplazar `TU_PASSWORD` por la contraseña del usuario `sa` de SQL Server.

User Secrets guarda esa conexión en tu computadora, fuera del proyecto. Así, cada integrante puede usar su contraseña sin compartirla ni subirla a GitHub.

Una vez configurada la conexión y con SQL Server funcionando, se puede iniciar el servicio desde Visual Studio con F5.