using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

public class HerramientasPresupuestosTests
{
    private static readonly Guid ObraId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly PartidaPresupuestariaAsistente Partida = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), ObraId, "Estructura", "EST-01", "Hormigón",
        100000m, 25000m, 10000m, 75000m, "ARS");

    private sealed class ConsultorFalso : IConsultorPresupuestos
    {
        public ResultadoPartidasPresupuestarias Resultado { get; set; } =
            ResultadoPartidasPresupuestarias.Bien(new[] { Partida }, 1);
        public int Llamadas { get; private set; }
        public Guid? UltimaObraId { get; private set; }
        public string? UltimaEtapa { get; private set; }

        public Task<ResultadoPartidasPresupuestarias> ListarPartidasAsync(Guid obraId, string? etapa, CancellationToken ct)
        {
            Llamadas++;
            UltimaObraId = obraId;
            UltimaEtapa = etapa;
            return Task.FromResult(Resultado);
        }
    }

    [Theory]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GUID_invalido_o_vacio_no_consulta_el_gateway(string obraId)
    {
        var consultor = new ConsultorFalso();
        var resultado = await new HerramientasPresupuestos(consultor).ConsultarAsync(obraId, null, default);

        Assert.False(resultado.Ok);
        Assert.Equal("argumentos_invalidos", resultado.Datos!["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.Llamadas);
    }

    [Fact]
    public async Task Consulta_valida_envia_obra_etapa_y_devuelve_los_datos_de_la_partida()
    {
        var consultor = new ConsultorFalso();
        var resultado = await new HerramientasPresupuestos(consultor)
            .ConsultarAsync(ObraId.ToString(), "Estructura", default);

        Assert.True(resultado.Ok);
        Assert.Equal(ObraId, consultor.UltimaObraId);
        Assert.Equal("Estructura", consultor.UltimaEtapa);
        var partida = resultado.Datos["partidas"]![0]!;
        Assert.Equal(Partida.Id.ToString(), partida["id"]!.GetValue<string>());
        Assert.Equal(Partida.ObraId.ToString(), partida["obraId"]!.GetValue<string>());
        Assert.Equal("Estructura", partida["etapa"]!.GetValue<string>());
        Assert.Equal("EST-01", partida["codigoPartida"]!.GetValue<string>());
        Assert.Equal("Hormigón", partida["descripcion"]!.GetValue<string>());
        Assert.Equal(100000m, partida["montoAsignado"]!.GetValue<decimal>());
        Assert.Equal(25000m, partida["montoComprometido"]!.GetValue<decimal>());
        Assert.Equal(10000m, partida["montoEjecutado"]!.GetValue<decimal>());
        Assert.Equal(75000m, partida["montoDisponible"]!.GetValue<decimal>());
        Assert.Equal("ARS", partida["moneda"]!.GetValue<string>());
        Assert.Equal(1, resultado.Datos["total"]!.GetValue<int>());
        Assert.Equal(1, resultado.Datos["mostradas"]!.GetValue<int>());
        Assert.False(resultado.Datos["hayMas"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Etapa_vacia_se_envia_como_filtro_nulo()
    {
        var consultor = new ConsultorFalso();

        await new HerramientasPresupuestos(consultor).ConsultarAsync(ObraId.ToString(), "  ", default);

        Assert.Null(consultor.UltimaEtapa);
    }

    [Fact]
    public async Task Lista_vacia_devuelve_cero_partidas()
    {
        var consultor = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(Array.Empty<PartidaPresupuestariaAsistente>(), 0)
        };

        var resultado = await new HerramientasPresupuestos(consultor)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.True(resultado.Ok);
        Assert.Empty(resultado.Datos["partidas"]!.AsArray());
        Assert.Equal(0, resultado.Datos["total"]!.GetValue<int>());
        Assert.Equal(0, resultado.Datos["mostradas"]!.GetValue<int>());
        Assert.False(resultado.Datos["hayMas"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Indica_total_mostradas_y_hayMas_cuando_hay_paginacion_pendiente()
    {
        var consultor = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(new[] { Partida }, 8)
        };

        var resultado = await new HerramientasPresupuestos(consultor)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.Equal(8, resultado.Datos["total"]!.GetValue<int>());
        Assert.Equal(1, resultado.Datos["mostradas"]!.GetValue<int>());
        Assert.True(resultado.Datos["hayMas"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Propaga_el_error_del_gateway()
    {
        var consultor = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Falla("sin_permiso")
        };

        var resultado = await new HerramientasPresupuestos(consultor)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.False(resultado.Ok);
        Assert.Equal("sin_permiso", resultado.Datos["error"]!.GetValue<string>());
    }
}
