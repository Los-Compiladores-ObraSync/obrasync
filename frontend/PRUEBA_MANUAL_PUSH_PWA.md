# Prueba manual: notificaciones push de ObraSync

Estas verificaciones requieren el backend #227 disponible, claves VAPID configuradas, HTTPS (o localhost permitido por el navegador) y un dispositivo compatible. Vitest valida la integración del frontend, pero no el envío real del servicio Push.

## Android

- Instalar ObraSync como PWA e iniciar sesión.
- En Inicio, activar avisos y aceptar el permiso.
- Con la aplicación cerrada, enviar una notificación desde el backend; comprobar que aparece en el sistema.
- Tocar la notificación y comprobar que abre la ruta indicada.
- Repetir con la aplicación en segundo plano.
- Repetir con la aplicación abierta y visible; confirmar que no aparece una notificación de sistema adicional a la campanita.
- Desactivar avisos y comprobar que deja de recibirlos.

## iPhone

- Verificar iOS 16.4 o superior e instalar ObraSync en la pantalla de inicio.
- Abrir la PWA instalada, iniciar sesión, activar avisos y aceptar el permiso.
- Repetir los casos cerrada, segundo plano y abierta visible de Android.
- Tocar la notificación y verificar que abre la ruta correcta.
- Desactivar avisos.

En Safari abierto como pestaña, la interfaz debe indicar que hay que instalar la PWA desde “Agregar a pantalla de inicio”.
