# Recepción y Presupuesto

Microservicio encargado del registro y consulta de recepciones de materiales (remitos en obra) y control presupuestario de las partidas.

---

## Cómo levantar el servicio con Docker

Requisitos: Docker Desktop abierto y funcionando.

Desde la raíz del repositorio (`obrasync/`):

1. Asegurarse de tener creado el archivo `.env` en la raíz con las variables `SQL_SA_PASSWORD` y `JWT_SECRET` (ver `.env.example`).
2. Ejecutar:

   ```bash
   docker compose up -d --build
   ```

3. **Tiempo aproximado de espera:**  
   Alrededor de **30 a 60 segundos**. La instancia de SQL Server tarda aproximadamente 20 a 35 segundos en alcanzar el estado *healthy*; una vez saludable, el contenedor del microservicio arranca y aplica automáticamente las migraciones a `DB_Recepcion`.

4. **Verificar que esté funcionando:**
   - Salud: `http://localhost:5004/health`
   - Swagger: `http://localhost:5004/swagger`
   - Dentro del contenedor de Docker el servicio escucha en el puerto interno `8080`, mapeado al puerto `5004` del host.

---

## Configuración de Secretos y Variables Obligatorias

### 1. Secreto JWT (`JWT_SECRET`) - OBLIGATORIO

El servicio valida los tokens de autenticación emitidos por el microservicio de Usuarios. Para poder arrancar, **requiere obligatoriamente** la clave `Jwt:Secret` configurada. Si falta, el servicio se detiene inmediatamente con una excepción al iniciar.

> **IMPORTANTE:** El secreto debe ser una cadena segura de al menos 32 caracteres (256 bits para HMAC-SHA256) compartida por todo el equipo, y **NUNCA debe subirse al repositorio ni escribirse en `appsettings.json`**.

#### En desarrollo local:

Opción recomendada usando **.NET User Secrets** (los guarda de forma segura en tu computadora fuera del código):

```bash
# Dentro de services/recepcion-presupuesto/ObraSync.Recepcion.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "cambiar-este-secreto-por-uno-de-mas-de-32-caracteres"
```

O definiéndolo mediante variable de entorno en tu terminal:

- **PowerShell:**
  ```powershell
  $env:Jwt__Secret = "cambiar-este-secreto-por-uno-de-mas-de-32-caracteres"
  ```
- **Bash / Linux / macOS:**
  ```bash
  export Jwt__Secret="cambiar-este-secreto-por-uno-de-mas-de-32-caracteres"
  ```

### 2. Cadena de Conexión a la Base de Datos

La cadena base a `DB_Recepcion` en [appsettings.json](file:///e:/facultad/proyecto%20Obrasync/obrasync/services/recepcion-presupuesto/ObraSync.Recepcion.Api/appsettings.json) no incluye contraseñas. Debe configurarse la contraseña de tu SQL Server local:

```bash
# Dentro de services/recepcion-presupuesto/ObraSync.Recepcion.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=DB_Recepcion;User Id=sa;Password=TU_PASSWORD;TrustServerCertificate=True;"
```

Reemplazar `TU_PASSWORD` por la contraseña del usuario `sa` de tu SQL Server.

---

## Cómo ejecutar el servicio localmente (sin Docker)

1. Tener una instancia de SQL Server funcionando en el puerto `1433`.
2. Tener configurados `Jwt:Secret` y `ConnectionStrings:DefaultConnection` en `user-secrets`.
3. Desde la carpeta `services/recepcion-presupuesto/ObraSync.Recepcion.Api`:

   ```bash
   dotnet run --launch-profile http
   ```

   (o en Visual Studio / IDE, presionar **F5** seleccionando el perfil `http`).

4. El servicio escuchará en `http://localhost:5004` y la documentación interactiva estará disponible en `http://localhost:5004/swagger`.

---

## Cómo ejecutar los tests

Para correr toda la suite de pruebas automatizadas del microservicio (pruebas de dominio, controladores y pruebas de integración):

Desde la raíz del repositorio:
```bash
dotnet test "services/recepcion-presupuesto/ObraSync.Recepcion.sln"
```

O desde la carpeta de pruebas `services/recepcion-presupuesto/ObraSync.Recepcion.Tests`:
```bash
dotnet test
```

---

## Documentación de la API (Swagger UI y compatibilidad OpenAPI)

La documentación interactiva de la API está disponible en `/swagger` al ejecutar en entorno de desarrollo.

### Compatibilidad de versiones de OpenAPI y dependencias NuGet

- **Dependencia utilizada:** Únicamente `Swashbuckle.AspNetCore` (6.6.2).
- **Paquete retirado:** `Microsoft.AspNetCore.OpenApi` (8.0.31).

#### Motivo:
El paquete `Microsoft.AspNetCore.OpenApi` está diseñado para Minimal APIs (`.WithOpenApi()`) y no es requerido para APIs basadas en controladores MVC. Dicho paquete arrastra transitivamente la biblioteca `Microsoft.OpenApi (>= 1.6.30)`, la cual serializa el esquema bajo la especificación `"openapi": "3.0.4"`.

El visualizador integrado de Swagger UI incluido en `Swashbuckle.AspNetCore 6.6.2` valida la versión de OpenAPI mediante una expresión regular estricta (`/^3\.0\.([0123])(?:-rc[012])?$/`), la cual admite únicamente las versiones `3.0.0` a `3.0.3`. Al recibir `"openapi": "3.0.4"`, Swagger UI fallaba en el navegador mostrando:

> *Unable to render this definition: The provided definition does not specify a valid version field. Supported version fields are swagger: "2.0" and those that match openapi: 3.x.y (for example, openapi: 3.1.0).*

Al prescindir de `Microsoft.AspNetCore.OpenApi`, el proyecto resuelve `Microsoft.OpenApi 1.6.14` (dependencia nativa de Swashbuckle), emitiendo la especificación con `"openapi": "3.0.1"`, la cual es parseada y renderizada correctamente por la interfaz web de Swagger sin errores.

> **NOTA SOBRE LAS PRUEBAS DE INTEGRACIÓN:**  
> Las pruebas de integración se ejecutan utilizando el proveedor **InMemory** de Entity Framework Core (`Microsoft.EntityFrameworkCore.InMemory`) para aislar la suite y permitir su ejecución veloz sin requerir una instancia activa de SQL Server. Por diseño del proveedor en memoria, **estas pruebas no validan restricciones relacionales específicas del motor de SQL Server** (tales como claves foráneas relacionales, restricciones `MaxLength`, precisión y escala de tipos decimales, tipos de columna SQL ni bloqueos transaccionales).