using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Services;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

public class CrearCotizacionCommandHandler
{
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly ICotizacionValidadorService _validadorService;

    public CrearCotizacionCommandHandler(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository,
        ICotizacionValidadorService? validadorService = null)
    {
        if (licitacionRepository == null) throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
        _validadorService = validadorService ?? new CotizacionValidadorService(licitacionRepository, cotizacionRepository);
    }

    public async Task<CotizacionResponseDto> Handle(
        CrearCotizacionCommand command,
        CancellationToken cancellationToken = default)
    {
        var (_, cotizacion, dto) = await _validadorService.ValidarYConstruirAsync(
            command.LicitacionId,
            command.ProveedorId,
            command.Flete,
            command.PlazoEntregaDias,
            command.Items,
            cancellationToken,
            command.ProveedorNombre);

        await _cotizacionRepository.AddAsync(cotizacion, cancellationToken);
        await _cotizacionRepository.SaveChangesAsync(cancellationToken);

        return dto;
    }
}
