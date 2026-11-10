# Contrato de Token JWT - ObraSync
* **Emisor:** Servicio de Usuarios y Obras
* **Algoritmo:** HS256 (Firma simétrica con secreto compartido)
* **Claims incluidos:**
  * `sub`: GUID del usuario.
  * `email`: Correo electrónico del usuario.
  * `name`: Nombre y apellido.
  * `role`: Rol asignado (`Capataz`, `Comprador`, `Proveedor`, `Admin`).
  * `iss` (emisor): `obrasync-usuarios`.
  * `aud` (audiencia): `obrasync-services`.
* **Validación:** los 4 servicios validan firma, vencimiento, `iss` y `aud`. Un token sin esos valores, o con otros, se rechaza con 401.
* **Configuración:** los valores por defecto son los de arriba. Se pueden cambiar con las variables de entorno `Jwt__Issuer` y `Jwt__Audience`, que tienen que ser iguales en todos los servicios.
* **Vencimiento:** lo fija Usuarios (`Jwt__ExpirationMinutes`, 60 minutos por defecto).
