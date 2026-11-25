using System.Diagnostics.CodeAnalysis;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Recepciones;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Validators;

/// <summary>
/// Validador dedicado para las reglas de entrada, evidencias fotográficas, precondiciones de la orden de compra
/// y consistencia de renglones al registrar una recepción (SRP).
/// </summary>
public class CrearRecepcionCommandValidator : ICrearRecepcionCommandValidator
{
    public EntradaRecepcionValidada ValidarEntrada(CrearRecepcionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El usuario que registra la recepción es obligatorio.");
        }

        if (command.ObraId == Guid.Empty ||
            command.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("La obra y la orden de compra son obligatorias.");
        }

        if (command.Estado.HasValue && command.Estado.Value != EstadoRecepcion.Rechazada)
        {
            throw new ArgumentException("Solo se permite indicar explícitamente el estado Rechazada (4) para entregas con problema.");
        }

        bool esEntregaConProblema = command.Estado == EstadoRecepcion.Rechazada;
        string? motivoNormalizado = null;

        if (esEntregaConProblema)
        {
            motivoNormalizado = MotivosProblemaRecepcion.Normalizar(command.MotivoProblema);
            if (motivoNormalizado is null)
            {
                throw new ArgumentException(
                    $"El motivo del problema es obligatorio y debe ser uno de: {string.Join(", ", MotivosProblemaRecepcion.Permitidos)}.");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(command.MotivoProblema))
            {
                throw new ArgumentException("El motivo de problema solo puede informarse cuando el estado de la recepción es Rechazada.");
            }

            if (command.Fotos is { Count: > 0 })
            {
                throw new ArgumentException("Solo se pueden adjuntar fotos de evidencia en entregas con problema (Rechazada).");
            }
        }

        if (esEntregaConProblema)
        {
            if (!string.IsNullOrWhiteSpace(command.NumeroRemito) && command.NumeroRemito.Trim().Length > 50)
            {
                throw new ArgumentException("El número de remito no puede superar los 50 caracteres.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(command.NumeroRemito) ||
                command.NumeroRemito.Length > 50)
            {
                throw new ArgumentException("El número de remito es obligatorio y no puede superar los 50 caracteres.");
            }
        }

        if (command.FechaRecepcion == default)
        {
            throw new ArgumentException("La fecha de recepción es obligatoria.");
        }

        // Se permite un día de tolerancia por la diferencia horaria con UTC.
        DateTime fechaMaximaPermitida = DateTime.UtcNow.Date.AddDays(1);

        if (command.FechaRecepcion.Date > fechaMaximaPermitida)
        {
            throw new ArgumentException("La fecha de recepcion no puede ser futura.");
        }

        if (command.Observaciones?.Length > 500)
        {
            throw new ArgumentException("Las observaciones no pueden superar los 500 caracteres.");
        }

        if (!esEntregaConProblema && (command.Detalles == null || command.Detalles.Count == 0))
        {
            throw new ArgumentException("La recepción debe incluir al menos un material.");
        }

        var fotosValidadas = new List<FotoEvidenciaValidada>();
        if (command.Fotos is { Count: > 0 })
        {
            if (command.Fotos.Count > ValidadorFotoEvidencia.MaxFotosPorRecepcion)
            {
                throw new ArgumentException($"Se permiten como máximo {ValidadorFotoEvidencia.MaxFotosPorRecepcion} fotos por entrega con problema.");
            }

            foreach (var fotoEntrada in command.Fotos)
            {
                var (contentType, nombreSaneado) = ValidadorFotoEvidencia.Validar(
                    fotoEntrada.Contenido,
                    fotoEntrada.NombreArchivo);

                fotosValidadas.Add(new FotoEvidenciaValidada(contentType, nombreSaneado, fotoEntrada.Contenido));
            }
        }

        return new EntradaRecepcionValidada(esEntregaConProblema, motivoNormalizado, fotosValidadas);
    }

    public void ValidarOrdenCompra(
        CrearRecepcionCommand command,
        [NotNull] OrdenCompra? ordenCompra)
    {
        if (ordenCompra == null)
        {
            throw new ArgumentException("La orden de compra no existe.");
        }

        if (ordenCompra.ObraId != command.ObraId)
        {
            throw new ArgumentException("La orden de compra no pertenece a la obra indicada.");
        }

        if (ordenCompra.Estado == EstadoOrdenCompra.Bloqueada)
        {
            throw new RecepcionConflictoException("La orden de compra se encuentra bloqueada por desvío presupuestario y no permite recepciones.");
        }

        if (!ordenCompra.EstaDespachada)
        {
            throw new RecepcionConflictoException("La orden de compra todavía no fue despachada");
        }
    }

    public void ValidarDetalles(
        IReadOnlyCollection<CrearDetalleRecepcionRequest> detalles,
        OrdenCompra ordenCompra,
        IReadOnlyDictionary<Guid, decimal> saldosPendientes,
        bool esEntregaConProblema)
    {
        ArgumentNullException.ThrowIfNull(detalles);
        ArgumentNullException.ThrowIfNull(ordenCompra);
        ArgumentNullException.ThrowIfNull(saldosPendientes);

        if (detalles.Count != ordenCompra.Items.Count)
        {
            throw new ArgumentException("La recepción debe incluir todos los materiales de la orden de compra.");
        }

        bool articulosRepetidos = detalles
            .GroupBy(d => d.ArticuloId)
            .Any(grupo => grupo.Count() > 1);

        if (articulosRepetidos)
        {
            throw new ArgumentException("No se puede incluir un mismo artículo más de una vez.");
        }

        foreach (var detalleRequest in detalles)
        {
            var articuloComprado = ordenCompra.Items
                .FirstOrDefault(i => i.ArticuloId == detalleRequest.ArticuloId);

            if (articuloComprado == null)
            {
                throw new ArgumentException("Uno de los artículos no pertenece a la orden de compra.");
            }

            decimal cantidadPendiente = saldosPendientes.GetValueOrDefault(detalleRequest.ArticuloId, 0m);

            if (detalleRequest.CantidadEsperada != articuloComprado.Cantidad &&
                detalleRequest.CantidadEsperada != cantidadPendiente)
            {
                throw new ArgumentException("La cantidad esperada no coincide con la orden de compra.");
            }

            if (detalleRequest.CantidadRecibida < 0)
            {
                throw new ArgumentException("La cantidad recibida no puede ser negativa.");
            }

            if (string.Equals(articuloComprado.UnidadMedida, "BOLSA", StringComparison.OrdinalIgnoreCase)
                && detalleRequest.CantidadRecibida != decimal.Truncate(detalleRequest.CantidadRecibida))
            {
                throw new ArgumentException("Las bolsas se reciben en cantidades enteras: no se pueden recibir fracciones de bolsa.");
            }

            if (detalleRequest.CantidadRecibida > cantidadPendiente)
            {
                throw new ArgumentException("La cantidad recibida acumulada supera la cantidad pedida en la orden de compra.");
            }

            if (string.IsNullOrWhiteSpace(detalleRequest.DescripcionArticulo) ||
                detalleRequest.DescripcionArticulo.Length > 250 ||
                string.IsNullOrWhiteSpace(detalleRequest.UnidadMedida) ||
                detalleRequest.UnidadMedida.Length > 20 ||
                detalleRequest.Observacion?.Length > 500)
            {
                throw new ArgumentException("Los datos de uno de los materiales no son válidos.");
            }
        }

        if (!esEntregaConProblema && detalles.Sum(d => d.CantidadRecibida) <= 0)
        {
            throw new ArgumentException("La recepción debe registrar una cantidad recibida mayor a cero en al menos un material.");
        }
    }
}
