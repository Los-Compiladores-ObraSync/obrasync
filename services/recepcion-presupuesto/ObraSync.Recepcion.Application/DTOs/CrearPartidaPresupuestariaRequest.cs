using System;
using System.ComponentModel.DataAnnotations;

namespace ObraSync.Recepcion.Application.DTOs;

public class CrearPartidaPresupuestariaRequest
{
    [Required(ErrorMessage = "El identificador de la obra es obligatorio.")]
    public Guid ObraId { get; set; }

    [MaxLength(200, ErrorMessage = "La etapa no puede superar los 200 caracteres.")]
    public string? Etapa { get; set; }

    [Required(ErrorMessage = "El código de partida es obligatorio.")]
    [MaxLength(50, ErrorMessage = "El código de partida no puede superar los 50 caracteres.")]
    public string CodigoPartida { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción de la partida es obligatoria.")]
    [MaxLength(250, ErrorMessage = "La descripción no puede superar los 250 caracteres.")]
    public string Descripcion { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "El monto asignado debe ser mayor o igual a cero.")]
    public decimal MontoAsignado { get; set; }

    [MaxLength(10, ErrorMessage = "La moneda no puede superar los 10 caracteres.")]
    public string Moneda { get; set; } = "ARS";
}
