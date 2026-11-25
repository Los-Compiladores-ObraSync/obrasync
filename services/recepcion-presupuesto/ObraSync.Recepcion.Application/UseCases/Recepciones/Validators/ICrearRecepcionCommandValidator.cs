using System.Diagnostics.CodeAnalysis;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Validators;

public sealed record FotoEvidenciaValidada(
    string ContentType,
    string NombreArchivo,
    byte[] Contenido);

public readonly record struct EntradaRecepcionValidada(
    bool EsEntregaConProblema,
    string? MotivoNormalizado,
    IReadOnlyList<FotoEvidenciaValidada>? FotosValidadas);

/// <summary>
/// Abstracción dedicada a la validación de entrada y precondiciones del comando de creación de recepciones (SRP / DIP).
/// </summary>
public interface ICrearRecepcionCommandValidator
{
    EntradaRecepcionValidada ValidarEntrada(CrearRecepcionCommand command);

    void ValidarOrdenCompra(
        CrearRecepcionCommand command,
        [NotNull] OrdenCompra? ordenCompra);

    void ValidarDetalles(
        IReadOnlyCollection<CrearDetalleRecepcionRequest> detalles,
        OrdenCompra ordenCompra,
        IReadOnlyDictionary<Guid, decimal> saldosPendientes,
        bool esEntregaConProblema);
}
