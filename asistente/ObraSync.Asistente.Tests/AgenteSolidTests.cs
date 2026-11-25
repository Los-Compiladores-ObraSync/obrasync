using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using ObraSync.Asistente.Api.Mcp;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Application.Generadas;

namespace ObraSync.Asistente.Tests;

/// <summary>
/// Pruebas de cumplimiento de principios SOLID (SRP, OCP, LSP, ISP, DIP) en el módulo Asistente.
/// </summary>
public class AgenteSolidTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public AgenteSolidTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", AsistenteEndpointTests.Secreto));
    }

    /// <summary>
    /// Nuevo grupo de herramientas enchufable sin modificar <see cref="Agente"/> ni <see cref="PerfilAsistente"/> (OCP + DIP).
    /// </summary>
    private sealed class GrupoClimaObraFalso : IGrupoHerramientas
    {
        public const string NombreHerramienta = "consultar_clima_obra";
        public int Ejecuciones { get; private set; }

        public bool Contiene(string nombre) =>
            string.Equals(nombre, NombreHerramienta, StringComparison.Ordinal);

        public bool PermiteRol(PerfilAsistente perfil, string nombre) =>
            Contiene(nombre) && perfil.Rol is PerfilAsistente.Capataz or PerfilAsistente.Admin;

        public IEnumerable<JsonNode> DefinicionesPara(PerfilAsistente perfil)
        {
            if (!PermiteRol(perfil, NombreHerramienta))
                yield break;

            yield return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = NombreHerramienta,
                    ["description"] = "Consulta el pronóstico de lluvia en la obra. Solo lectura.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["obra"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("obra")
                    }
                }
            };
        }

        public Task<ResultadoHerramienta> EjecutarAsync(
            PerfilAsistente perfil,
            string nombre,
            string argumentosJson,
            CancellationToken ct)
        {
            if (!Contiene(nombre))
                return Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"));
            if (!PermiteRol(perfil, nombre))
                return Task.FromResult(ResultadoHerramienta.Error("herramienta_no_permitida"));

            Ejecuciones++;
            return Task.FromResult(ResultadoHerramienta.Bien(new JsonObject
            {
                ["ok"] = true,
                ["pronostico"] = "Despejado, apto para hormigonar"
            }));
        }
    }

    private sealed class ModeloInspector : IModeloChat
    {
        private readonly Queue<RespuestaModelo> _respuestas;
        public List<List<string>> HerramientasPorLlamada { get; } = [];
        public List<JsonArray> MensajesPorLlamada { get; } = [];

        public ModeloInspector(params RespuestaModelo[] respuestas) => _respuestas = new(respuestas);

        public Task<RespuestaModelo> PedirAsync(JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
        {
            MensajesPorLlamada.Add((JsonArray)mensajes.DeepClone());
            HerramientasPorLlamada.Add(
                (herramientas ?? [])
                .Select(h => h!["function"]!["name"]!.GetValue<string>())
                .ToList());
            return Task.FromResult(_respuestas.Dequeue());
        }
    }

    // ── OCP + DIP en Agente ──────────────────────────────────────────────────

    [Fact]
    public async Task Agente_permite_enchufar_un_nuevo_IGrupoHerramientas_sin_modificar_Agente_ni_PerfilAsistente()
    {
        var grupoClima = new GrupoClimaObraFalso();
        var modelo = new ModeloInspector(
            Respuestas.PideHerramienta("{\"obra\":\"Torre Central\"}", nombre: GrupoClimaObraFalso.NombreHerramienta),
            Respuestas.Final("Está despejado para hormigonar."));

        var agente = new Agente(
            modelo,
            new IGrupoHerramientas[] { grupoClima },
            new ContextoFalso { Rol = PerfilAsistente.Capataz });

        var respuesta = await agente.ResponderAsync("¿Cómo viene el clima en Torre Central?");

        Assert.True(respuesta.Ok);
        Assert.Equal(1, grupoClima.Ejecuciones);
        Assert.Equal([GrupoClimaObraFalso.NombreHerramienta], modelo.HerramientasPorLlamada[0]);
        Assert.Contains("apto para hormigonar", modelo.MensajesPorLlamada[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Agente_bloquea_herramienta_de_grupo_personalizado_cuando_el_rol_no_esta_autorizado()
    {
        var grupoClima = new GrupoClimaObraFalso();
        var modelo = new ModeloInspector(
            Respuestas.PideHerramienta("{\"obra\":\"Torre Central\"}", nombre: GrupoClimaObraFalso.NombreHerramienta),
            Respuestas.Final("No tengo esa herramienta."));

        var agente = new Agente(
            modelo,
            new IGrupoHerramientas[] { grupoClima },
            new ContextoFalso { Rol = PerfilAsistente.Comprador });

        var respuesta = await agente.ResponderAsync("¿Va a llover en obra?");

        Assert.True(respuesta.Ok);
        Assert.Equal(0, grupoClima.Ejecuciones);
        Assert.Empty(modelo.HerramientasPorLlamada[0]);
        Assert.Contains("herramienta_no_permitida", modelo.MensajesPorLlamada[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Endpoint_chat_resuelve_grupos_adicionales_registrados_en_DI_sin_tocar_Agente()
    {
        var grupoClima = new GrupoClimaObraFalso();
        var modelo = new ModeloInspector(
            Respuestas.PideHerramienta("{\"obra\":\"Torre Central\"}", nombre: GrupoClimaObraFalso.NombreHerramienta),
            Respuestas.Final("Clima despejado."));

        var cliente = _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IModeloChat>();
            s.AddScoped<IModeloChat>(_ => modelo);
            s.AddScoped<IGrupoHerramientas>(_ => grupoClima);
        })).CreateClient();

        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/asistente/chat")
        {
            Content = JsonContent.Create(new { mensaje = "¿Cómo está el clima?" })
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AsistenteEndpointTests.CrearToken("Capataz"));

        var httpResp = await cliente.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.OK, httpResp.StatusCode);
        Assert.Equal(1, grupoClima.Ejecuciones);
        Assert.Contains(GrupoClimaObraFalso.NombreHerramienta, modelo.HerramientasPorLlamada[0]);
    }

    [Fact]
    public async Task Orden_exacto_de_definiciones_se_conserva_para_Admin_incluyendo_generadas()
    {
        const string contrato = """
        {
          "openapi": "3.0.4",
          "paths": {
            "/api/proveedores": {
              "get": { "summary": "Listar proveedores (Roles: Admin)" }
            }
          }
        }
        """;
        var clienteLectura = Substitute.For<IClienteApiLectura>();
        var generadas = new HerramientasGeneradas(GeneradorHerramientasOpenApi.Generar(contrato), clienteLectura);
        var modelo = new ModeloInspector(Respuestas.Final("ok"));

        var grupos = new IGrupoHerramientas[]
        {
            new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
            new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas()),
            new HerramientasRecepcion(new ConsultorRecepcionesFalso(), new ObrasFalsas()),
            new HerramientasLicitaciones(new ConsultorLicitacionesFalso(), new ObrasFalsas()),
            generadas
        };

        var agente = new Agente(modelo, grupos, new ContextoFalso { Rol = PerfilAsistente.Admin });
        await agente.ResponderAsync("hola");

        var ordenEsperado = new[]
        {
            "buscar_insumos",
            "listar_mis_obras",
            "armar_borrador_requisicion",
            "listar_requisiciones",
            "ver_requisicion",
            "preparar_aprobacion_requisicion",
            "preparar_rechazo_requisicion",
            "preparar_publicacion_licitacion",
            "consultar_presupuesto_obra",
            "listar_ordenes_pendientes",
            "preparar_recepcion",
            "listar_licitaciones",
            "ver_licitacion",
            "listar_cotizaciones",
            "preparar_adjudicacion",
            "consultar_proveedores"
        };

        Assert.Equal(ordenEsperado, modelo.HerramientasPorLlamada[0]);
    }

    // ── Compatibilidad hacia atrás (semántica exacta de errores) ──────────────

    [Theory]
    [InlineData("Capataz", "listar_requisiciones", "herramienta_no_disponible")]
    [InlineData("Capataz", "listar_ordenes_pendientes", "herramienta_no_disponible")]
    [InlineData("Capataz", "preparar_aprobacion_requisicion", "herramienta_no_permitida")]
    [InlineData("Comprador", "listar_licitaciones", "herramienta_no_disponible")]
    [InlineData("Comprador", "buscar_insumos", "herramienta_no_permitida")]
    [InlineData("Admin", "herramienta_inexistente", "herramienta_desconocida")]
    public async Task Constructor_compatible_conserva_codigos_no_disponible_no_permitida_y_desconocida(
        string rol,
        string herramienta,
        string codigoEsperado)
    {
        var modelo = new ModeloInspector(
            Respuestas.PideHerramienta("{}", nombre: herramienta),
            Respuestas.Final("entendido"));

        var agente = new Agente(
            modelo,
            new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
            compras: null,
            contexto: new ContextoFalso { Rol = rol },
            consumo: null,
            recepciones: null,
            licitaciones: null,
            generadas: null);

        var r = await agente.ResponderAsync("probando");

        Assert.True(r.Ok);
        var contenidoTool = JsonNode.Parse(modelo.MensajesPorLlamada[1].Last()!["content"]!.GetValue<string>())!;
        Assert.False(contenidoTool["ok"]!.GetValue<bool>());
        Assert.Equal(codigoEsperado, contenidoTool["error"]!.GetValue<string>());
    }

    // ── LSP: todos los grupos cumplen el contrato IGrupoHerramientas ──────────

    [Fact]
    public void Todos_los_grupos_implementan_IGrupoHerramientas_de_forma_consistente_por_perfil()
    {
        const string contrato = """
        {
          "openapi": "3.0.4",
          "paths": {
            "/api/auditoria": {
              "get": { "summary": "Consultar auditoría (Roles: Comprador, Admin)" }
            }
          }
        }
        """;

        IGrupoHerramientas[] grupos =
        [
            new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
            new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas()),
            new HerramientasRecepcion(new ConsultorRecepcionesFalso(), new ObrasFalsas()),
            new HerramientasLicitaciones(new ConsultorLicitacionesFalso(), new ObrasFalsas()),
            new HerramientasGeneradas(GeneradorHerramientasOpenApi.Generar(contrato), Substitute.For<IClienteApiLectura>())
        ];

        foreach (var rol in new[] { PerfilAsistente.Capataz, PerfilAsistente.Comprador, PerfilAsistente.Admin })
        {
            var perfil = PerfilAsistente.Para(rol)!;
            foreach (var grupo in grupos)
            {
                Assert.False(grupo.Contiene("no_existe_nunca"));
                Assert.False(grupo.PermiteRol(perfil, "no_existe_nunca"));

                foreach (var def in grupo.DefinicionesPara(perfil))
                {
                    var nombre = def["function"]!["name"]!.GetValue<string>();
                    Assert.True(grupo.Contiene(nombre));
                    Assert.True(grupo.PermiteRol(perfil, nombre));
                }
            }
        }
    }

    // ── SRP: AcumuladorBorradores inmutable ───────────────────────────────────

    [Fact]
    public void AcumuladorBorradores_es_inmutable_y_conserva_borradores_previos_entre_llamadas()
    {
        var vacio = AcumuladorBorradores.Vacio;
        Assert.False(vacio.TieneAlguno);

        var borradorReq = new BorradorRequisicion(
            BorradorRequisicion.AccionCrearRequisicion,
            "Nueva requisición",
            new BorradorResumen("Torre Central", "Estructura", []),
            new BorradorCrearRequisicion(Guid.NewGuid(), "Estructura", null, []));

        var paso1 = vacio.Acumular(new ResultadoHerramienta(true, new JsonObject { ["ok"] = true }, Borrador: borradorReq));
        var paso2 = paso1.Acumular(ResultadoHerramienta.Bien(new JsonObject { ["ok"] = true }));

        Assert.False(vacio.TieneAlguno);
        Assert.True(paso1.TieneAlguno);
        Assert.Same(borradorReq, paso2.Borrador);

        var respuesta = paso2.CrearRespuesta("Listo", iteraciones: 2, herramientas: 2, tokens: 120);
        Assert.True(respuesta.Ok);
        Assert.Same(borradorReq, respuesta.Borrador);
        Assert.Equal(120, respuesta.Tokens);
    }

    // ── DIP + ISP + OCP en MCP y HerramientasCompras ──────────────────────────

    [Fact]
    public async Task HerramientasCompras_depende_de_la_abstraccion_IConsultaPresupuestoObra()
    {
        var consultaFalsa = Substitute.For<IConsultaPresupuestoObra>();
        consultaFalsa.ConsultarAsync(ObrasFalsas.IdTorre.ToString(), "Estructura", Arg.Any<CancellationToken>())
            .Returns(ResultadoHerramienta.Bien(new JsonObject { ["ok"] = true, ["fuente"] = "abstraccion" }));

        var compras = new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas(), consultaFalsa);
        var res = await compras.EjecutarAsync(
            HerramientasCompras.ConsultarPresupuesto,
            "{\"obra\":\"Torre Central\",\"etapa\":\"Estructura\"}",
            default);

        Assert.True(res.Ok);
        Assert.Equal("abstraccion", res.Datos["fuente"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clases_MCP_dependen_solo_de_sus_interfaces_segregadas()
    {
        var catalogo = Substitute.For<IHerramientasCatalogo>();
        catalogo.EjecutarAsync(HerramientasCatalogo.BuscarInsumos, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoHerramienta.Bien(new JsonObject { ["ok"] = true, ["origen"] = "catalogo_abstracto" }));

        var presupuestos = Substitute.For<IConsultaPresupuestoObra>();
        presupuestos.ConsultarAsync("id-obra", "Estructura", Arg.Any<CancellationToken>())
            .Returns(ResultadoHerramienta.Bien(new JsonObject { ["ok"] = true, ["origen"] = "presupuesto_abstracto" }));

        var mcpCatalogo = new HerramientasMcp(catalogo);
        var mcpPresupuestos = new HerramientasPresupuestosMcp(presupuestos);

        var jsonCat = await mcpCatalogo.BuscarInsumos("cemento");
        var jsonPres = await mcpPresupuestos.ConsultarPartidasPresupuestarias("id-obra", "Estructura");

        Assert.Contains("catalogo_abstracto", jsonCat);
        Assert.Contains("presupuesto_abstracto", jsonPres);
    }

    [Fact]
    public void PoliticaRutaMcp_permite_extender_nuevas_rutas_con_lista_blanca_sin_filtrar_herramientas_entre_rutas()
    {
        var politicas = FiltroHerramientasMcpPorRuta.PoliticasPorDefecto
            .Append(new PoliticaRutaMcp(
                "/api/mcp/licitaciones",
                "Comprador,Admin",
                [HerramientasLicitaciones.ListarLicitaciones]))
            .ToList();

        var original = FiltroHerramientasMcpPorRuta.ResolverPolitica(new PathString("/api/mcp"), politicas)!;
        var presupuestos = FiltroHerramientasMcpPorRuta.ResolverPolitica(new PathString("/api/mcp/presupuestos"), politicas)!;
        var licitaciones = FiltroHerramientasMcpPorRuta.ResolverPolitica(new PathString("/api/mcp/licitaciones"), politicas)!;
        var inexistente = FiltroHerramientasMcpPorRuta.ResolverPolitica(new PathString("/api/mcp/otra"), politicas);

        Assert.Null(inexistente);

        // La nueva herramienta solo es válida en su nueva ruta y no se filtra a /api/mcp ni a /api/mcp/presupuestos.
        Assert.True(licitaciones.PermiteHerramienta(HerramientasLicitaciones.ListarLicitaciones));
        Assert.False(original.PermiteHerramienta(HerramientasLicitaciones.ListarLicitaciones));
        Assert.False(presupuestos.PermiteHerramienta(HerramientasLicitaciones.ListarLicitaciones));
        Assert.False(licitaciones.PermiteHerramienta(HerramientasCatalogo.BuscarInsumos));
    }
}
