# Contrato de Token JWT - ObraSync
* **Emisor:** Servicio de Usuarios y Obras
* **Algoritmo:** HS256 (Firma simétrica con secreto compartido)
* **Claims incluidos:**
  * `sub`: GUID del usuario.
  * `email`: Correo electrónico del usuario.
  * `name`: Nombre y apellido.
  * `role`: Rol asignado (`Capataz`, `Comprador`, `Proveedor`, `Admin`).