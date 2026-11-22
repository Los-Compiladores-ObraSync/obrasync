# Servicio de Usuarios y Obras (`ObraSync.Usuarios.Api`)

Microservicio ASP.NET Core (.NET 8) encargado de autenticación/autorización (ASP.NET Core Identity + JWT), gestión de obras, plantillas de obra y notificaciones Web Push (`#227`).

## Variables de entorno requeridas y opcionales

| Variable | Obligatoria | Descripción |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Sí (en SQL Server) | Cadena de conexión a `DB_Auth`. |
| `Jwt__Secret` | Sí | Secreto compartido HS256 (mínimo 32 caracteres). |
| `RabbitMQ__Host` / `RabbitMQ__Username` / `RabbitMQ__Password` | Sí (salvo `RabbitMQ__UseInMemory=true`) | Conexión al broker RabbitMQ para consumir eventos de negocio. |
| `Vapid__PublicKey` | Para activar Web Push | Clave pública VAPID en Base64 URL-safe (`VAPID_PUBLIC_KEY` en `.env`). |
| `Vapid__PrivateKey` | Para activar Web Push | Clave privada VAPID en Base64 URL-safe (`VAPID_PRIVATE_KEY` en `.env`). Nunca guardar en el repo. |
| `Vapid__Subject` | Opcional | Contacto VAPID con formato `mailto:correo@dominio` o URL `https://...` (default `mailto:soporte@obrasync.local`). |

## Cómo generar las claves VAPID (#227)

Ejecutá una sola vez en tu máquina (requiere Node.js):

```bash
npx web-push generate-vapid-keys
```

El comando imprime un par `Public Key` y `Private Key`. Copiá ambos valores en tu archivo `.env` local (ignorado por Git):

```env
VAPID_PUBLIC_KEY=<tu_public_key_generada>
VAPID_PRIVATE_KEY=<tu_private_key_generada>
VAPID_SUBJECT=mailto:soporte@obrasync.local
```

Si levantás el servicio sin estas variables cargadas, la API arranca igual sin fallar: `GET /api/usuarios/notificaciones/vapid-public-key` devolverá `503 Service Unavailable` (`ProblemDetails`) y los consumidores registrarán una advertencia sin romper las colas de RabbitMQ.
