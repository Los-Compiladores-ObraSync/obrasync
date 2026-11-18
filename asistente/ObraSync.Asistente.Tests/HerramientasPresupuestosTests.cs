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

    private sealed class OrdenesFalsas : IConsultorOrdenesBloqueadas
    {
        public Func<Guid, int, int, ResultadoOrdenesBloqueadas> Responder { get; set; } = (_, _, _) =>
            ResultadoOrdenesBloqueadas.Bien(Array.Empty<OrdenCompraBloqueadaAsistente>(), 0);
        public List<int> Paginas { get; } = [];

        public Task<ResultadoOrdenesBloqueadas> ListarAsync(Guid obraId, int pagina, int tamano, CancellationToken ct)
        {
            Paginas.Add(pagina);
            return Task.FromResult(Responder(obraId, pagina, tamano));
        }
    }

    private static HerramientasPresupuestos Herramienta(ConsultorFalso partidas, OrdenesFalsas? ordenes = null) =>
        new(partidas, ordenes ?? new OrdenesFalsas());

    [Theory]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GUID_invalido_o_vacio_no_consulta_el_gateway(string obraId)
    {
        var consultor = new ConsultorFalso();
        var resultado = await Herramienta(consultor).ConsultarAsync(obraId, null, default);

        Assert.False(resultado.Ok);
        Assert.Equal("argumentos_invalidos", resultado.Datos!["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.Llamadas);
    }

    [Fact]
    public async Task Consulta_valida_envia_obra_etapa_y_devuelve_los_datos_de_la_partida()
    {
        var consultor = new ConsultorFalso();
        var resultado = await Herramienta(consultor)
            .ConsultarAsync(ObraId.ToString(), "Estructura", default);

        Assert.True(resultado.Ok);
        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
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
        Assert.True(resultado.Datos["hayMasConocido"]!.GetValue<bool>());
        Assert.True(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.Empty(resultado.Datos["ordenesBloqueadas"]!.AsArray());
    }

    [Fact]
    public async Task Etapa_vacia_se_envia_como_filtro_nulo()
    {
        var consultor = new ConsultorFalso();

        await Herramienta(consultor).ConsultarAsync(ObraId.ToString(), "  ", default);

        Assert.Null(consultor.UltimaEtapa);
    }

    [Fact]
    public async Task Lista_vacia_devuelve_cero_partidas()
    {
        var consultor = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(Array.Empty<PartidaPresupuestariaAsistente>(), 0)
        };

        var resultado = await Herramienta(consultor)
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

        var resultado = await Herramienta(consultor)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.Equal(8, resultado.Datos["total"]!.GetValue<int>());
        Assert.Equal(1, resultado.Datos["mostradas"]!.GetValue<int>());
        Assert.True(resultado.Datos["hayMas"]!.GetValue<bool>());
        Assert.True(resultado.Datos["hayMasConocido"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Propaga_el_error_del_gateway()
    {
        var consultor = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Falla("sin_permiso")
        };

        var resultado = await Herramienta(consultor)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.False(resultado.Ok);
        Assert.False(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.False(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.Equal("sin_permiso", resultado.Datos["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Integra_partidas_y_ordenes_bloqueadas_con_datos_oficiales_y_sin_campos_inventados()
    {
        var consultor = new ConsultorFalso();
        var fecha = new DateTime(2026, 11, 16, 10, 30, 0, DateTimeKind.Utc);
        var orden = new OrdenCompraBloqueadaAsistente(
            Guid.Parse("33333333-3333-3333-3333-333333333333"), ObraId, "Estructura", 120m, 100m,
            25m, 145m, 45m, "exceso de presupuesto", Guid.Parse("44444444-4444-4444-4444-444444444444"), fecha);
        var bloqueadas = new OrdenesFalsas
        {
            Responder = (_, _, _) => ResultadoOrdenesBloqueadas.Bien(new[] { orden }, 1)
        };

        var resultado = await Herramienta(consultor, bloqueadas).ConsultarAsync(ObraId.ToString(), null, default);

        Assert.True(resultado.Ok);
        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.Single(resultado.Datos["partidas"]!.AsArray());
        var jsonOrden = Assert.Single(resultado.Datos["ordenesBloqueadas"]!.AsArray());
        Assert.Equal(orden.OrdenCompraId.ToString(), jsonOrden!["ordenCompraId"]!.GetValue<string>());
        Assert.Equal(orden.PartidaId!.Value.ToString(), jsonOrden["partidaId"]!.GetValue<string>());
        Assert.Equal(145m, jsonOrden["montoProyectado"]!.GetValue<decimal>());
        Assert.Equal(45m, jsonOrden["exceso"]!.GetValue<decimal>());
        Assert.Equal("exceso de presupuesto", jsonOrden["motivo"]!.GetValue<string>());
        Assert.Equal(fecha, jsonOrden["fechaBloqueo"]!.GetValue<DateTime>());
        Assert.Null(jsonOrden["montoEjecutado"]);
        Assert.Null(jsonOrden["montoDisponible"]);
        Assert.Null(jsonOrden["moneda"]);
        Assert.True(resultado.Datos["ordenesBloqueadasPaginacion"]!["completo"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Filtro_etapa_recupera_todas_las_paginas_antes_de_filtrar_ordenes()
    {
        var consultor = new ConsultorFalso();
        var primera = new OrdenCompraBloqueadaAsistente(Guid.NewGuid(), ObraId, "Pintura", 1m, 1m, 0m, 1m, 0m, "x", null, DateTime.UtcNow);
        var segunda = new OrdenCompraBloqueadaAsistente(Guid.NewGuid(), ObraId, "Estructura", 2m, 1m, 0m, 2m, 1m, "y", null, DateTime.UtcNow);
        var bloqueadas = new OrdenesFalsas
        {
            Responder = (_, pagina, tamano) => pagina switch
            {
                1 => ResultadoOrdenesBloqueadas.Bien(new[] { primera }, 2),
                2 => ResultadoOrdenesBloqueadas.Bien(new[] { segunda }, 2),
                _ => throw new InvalidOperationException()
            }
        };

        var resultado = await Herramienta(consultor, bloqueadas).ConsultarAsync(ObraId.ToString(), "Estructura", default);

        Assert.Equal(new[] { 1, 2 }, bloqueadas.Paginas);
        var orden = Assert.Single(resultado.Datos["ordenesBloqueadas"]!.AsArray());
        Assert.Equal("Estructura", orden!["etapa"]!.GetValue<string>());
        Assert.True(resultado.Datos["ordenesBloqueadasPaginacion"]!["completo"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Limite_de_paginas_informa_resultado_incompleto_y_no_omite_la_indicacion()
    {
        var consultor = new ConsultorFalso();
        var bloqueadas = new OrdenesFalsas
        {
            Responder = (_, pagina, tamano) => ResultadoOrdenesBloqueadas.Bien(
                new[] { new OrdenCompraBloqueadaAsistente(Guid.NewGuid(), ObraId, "Estructura", 1m, 1m, 0m, 1m, 0m, "x", null, DateTime.UtcNow) },
                HerramientasPresupuestos.MaxPaginasOrdenesBloqueadas * tamano + 1)
        };

        var resultado = await Herramienta(consultor, bloqueadas).ConsultarAsync(ObraId.ToString(), "Estructura", default);

        Assert.False(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.True(resultado.Ok);
        Assert.False(resultado.Datos["ordenesBloqueadasPaginacion"]!["completo"]!.GetValue<bool>());
        Assert.Equal("limite_paginas_alcanzado", resultado.Datos["ordenesBloqueadasPaginacion"]!["error"]!.GetValue<string>());
        Assert.Equal(HerramientasPresupuestos.MaxPaginasOrdenesBloqueadas, bloqueadas.Paginas.Count);
    }

    [Fact]
    public async Task Error_del_gateway_de_ordenes_se_muestra_como_resultado_incompleto()
    {
        var bloqueadas = new OrdenesFalsas
        {
            Responder = (_, _, _) => ResultadoOrdenesBloqueadas.Falla("sin_permiso")
        };

        var resultado = await Herramienta(new ConsultorFalso(), bloqueadas)
            .ConsultarAsync(ObraId.ToString(), null, default);

        Assert.True(resultado.Ok);
        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.False(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.False(resultado.Datos["ordenesBloqueadasPaginacion"]!["completo"]!.GetValue<bool>());
        Assert.Equal("sin_permiso", resultado.Datos["ordenesBloqueadasPaginacion"]!["error"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("TERMINACIÓN DE OBRA", "terminacion de obra", "  Terminacion   de obra ")]
    [InlineData("Obra Gruesa", "obra gruesa", "OBRA GRUESA")]
    [InlineData("Obra Gruesa", "OBRA  GRUESA", " obra   gruesa ")]
    public async Task Filtro_etapa_normaliza_tildes_mayusculas_y_espacios_para_partidas_y_ordenes(
        string etapaPartida,
        string etapaOrden,
        string filtro)
    {
        var partidas = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(new[]
            {
                Partida with { Etapa = etapaPartida }
            }, 1)
        };
        var orden = new OrdenCompraBloqueadaAsistente(
            Guid.NewGuid(), ObraId, etapaOrden, 1m, 1m, 0m, 1m, 0m, "motivo", null, DateTime.UtcNow);
        var ordenes = new OrdenesFalsas
        {
            Responder = (_, _, _) => ResultadoOrdenesBloqueadas.Bien(new[] { orden }, 1)
        };

        var resultado = await Herramienta(partidas, ordenes)
            .ConsultarAsync(ObraId.ToString(), filtro, default);

        Assert.Single(resultado.Datos["partidas"]!.AsArray());
        var ordenDevuelta = Assert.Single(resultado.Datos["ordenesBloqueadas"]!.AsArray());
        Assert.Equal(etapaOrden, ordenDevuelta!["etapa"]!.GetValue<string>());
        Assert.True(resultado.Datos["ordenesBloqueadasPaginacion"]!["completo"]!.GetValue<bool>());
        Assert.True(resultado.Datos["completo"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public async Task Total_de_partidas_distinto_de_elementos_recibidos_no_es_completo(int total)
    {
        var partidas = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(new[] { Partida }, total)
        };

        var resultado = await Herramienta(partidas).ConsultarAsync(ObraId.ToString(), null, default);

        Assert.True(resultado.Ok);
        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.False(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.False(resultado.Datos["partidasCompletas"]!.GetValue<bool>());
        Assert.Equal("total_partidas_no_coincide", resultado.Datos["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Total_de_partidas_desconocido_conserva_hayMas_booleano_y_declara_que_se_desconoce()
    {
        var partidas = new ConsultorFalso
        {
            Resultado = ResultadoPartidasPresupuestarias.Bien(new[] { Partida }, null)
        };

        var resultado = await Herramienta(partidas).ConsultarAsync(ObraId.ToString(), null, default);

        Assert.True(resultado.Datos["ok"]!.GetValue<bool>());
        Assert.False(resultado.Datos["hayMas"]!.GetValue<bool>());
        Assert.False(resultado.Datos["hayMasConocido"]!.GetValue<bool>());
        Assert.False(resultado.Datos["partidasCompletas"]!.GetValue<bool>());
        Assert.False(resultado.Datos["completo"]!.GetValue<bool>());
        Assert.Equal("total_partidas_no_disponible", resultado.Datos["error"]!.GetValue<string>());
    }
}
