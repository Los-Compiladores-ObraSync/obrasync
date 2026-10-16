using Microsoft.AspNetCore.Identity;

namespace ObraSync.Usuarios.Domain.Entities;

// Tabla de roles de usuario en la BD

public class AppRole : IdentityRole<Guid>
{
    public AppRole() : base() { }

    public AppRole(string roleName) : base(roleName) { }
}