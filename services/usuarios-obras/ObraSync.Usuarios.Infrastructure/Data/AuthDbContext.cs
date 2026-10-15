using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Infrastructure.Data;

/// DbContext de autenticación utilizando IdentityDbContext configurado con Guid como PK.

public class AuthDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            // Renombro AspNetUsers a Usuarios
            entity.ToTable("Usuarios");

            entity.Property(u => u.NombreCompleto)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(u => u.FechaCreacion)
                .HasDefaultValueSql("GETUTCDATE()");
        });
    }
}