using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Application.Generadas;

namespace ObraSync.Asistente.Tests;

/// <summary>Tarjeta #151: herramientas de solo lectura generadas desde los contratos OpenAPI.</summary>
public class HerramientasGeneradasTests
{
    private const string Uuid = "11111111-1111-1111-1111-111111111111";

    /// <summary>Contrato de ejemplo chico: un GET con parámetro de ruta, uno con filtros, uno anónimo, uno sin roles y un POST.</summary>
    private const string ContratoEjemplo = """
    {
      "openapi": "3.0.4",
      "paths": {
        "/api/ejemplos/{id}": {
          "get": {
            "summary": "Ver un ejemplo (Roles: Comprador, Admin)",
            "parameters": [
              { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } }
            ]
          }
        },
        "/api/ejemplos": {
          "get": {
            "summary": "Listar ejemplos (Roles: Capataz, Comprador, Admin)",
            "parameters": [
              { "name": "estado", "in": "query", "schema": { "type": "string", "enum": ["Abierta", "Cerrada"] } },
              { "name": "buscar", "in": "query", "schema": { "type": "string" } },
              { "name": "pagina", "in": "query", "schema": { "type": "integer", "minimum": 1 } },
              { "name": "tamano", "in": "query", "schema": { "type": "integer", "minimum": 1, "maximum": 50 } }
            ]
          },
          "post": { "summary": "Crear un ejemplo (Roles: Admin)" }
        },
        "/api/ejemplos/publico/{hash}": {
          "get": {
            "summary": "Ver algo sin sesión (Público / AllowAnonymous)",
            "parameters": [ { "name": "hash", "in": "path", "required": true, "schema": { "type": "string" } } ]
          }
        },
        "/api/sin-roles": {
          "get": { "summary": "Un GET que no declara roles" }
        },
        "/api/con-descripcion": {
          "get": {
            "summary": "Consultar algo",
            "description": "Roles permitidos: Comprador y Admin (Capataz y Proveedor reciben 403 Forbidden)."
          }
        }
      }
    }
    """;

    private sealed class ClienteFalso : IClienteApiLectura
    {
        public List<string> Pedidos { get; } = new();
        public RespuestaApiLectura Respuesta { get; set; } = RespuestaApiLectura.Bien(new JsonArray(new JsonObject { ["x"] = 1 }), 7);

        public Task<RespuestaApiLectura> GetAsync(string rutaYConsulta, CancellationToken ct)
        {
            Pedidos.Add(rutaYConsulta);
            return Task.FromResult(Respuesta);
        }
    }

    private static HerramientasGeneradas Armar(out ClienteFalso cliente)
    {
        cliente = new ClienteFalso();
        return new HerramientasGeneradas(GeneradorHerramientasOpenApi.Generar(ContratoEjemplo), cliente);
    }

    private static string? Error(ResultadoHerramienta r) => r.Datos["error"]?.GetValue<string>();

    // ── Generador ──────────────────────────────────────────────────────────────

    [Fact]
    public void Genera_una_herramienta_por_cada_GET_y_nunca_por_otros_metodos()
    {
        var nombres = GeneradorHerramientasOpenApi.Generar(ContratoEjemplo).Select(h => h.Nombre).ToList();

        Assert.Contains("consultar_ejemplos", nombres);
        Assert.Contains("consultar_ejemplos_por_id", nombres);
        Assert.Equal(4, nombres.Count); // ejemplos, ejemplos/{id}, sin-roles, con-descripcion (el POST y el anónimo no)
    }

    [Fact]
    public void Un_GET_publico_o_anonimo_no_se_genera()
    {
        var nombres = GeneradorHerramientasOpenApi.Generar(ContratoEjemplo).Select(h => h.Nombre);
        Assert.DoesNotContain(nombres, n => n.Contains("publico"));
    }

    [Fact]
    public void Los_roles_salen_del_resumen_o_de_la_descripcion_y_sin_datos_queda_solo_Admin()
    {
        var h = GeneradorHerramientasOpenApi.Generar(ContratoEjemplo).ToDictionary(x => x.Nombre);

        Assert.Equal(new[] { "Capataz", "Comprador", "Admin" }.OrderBy(x => x), h["consultar_ejemplos"].Roles.OrderBy(x => x));
        Assert.Equal(new[] { "Comprador", "Admin" }.OrderBy(x => x), h["consultar_con_descripcion"].Roles.OrderBy(x => x));
        Assert.Equal(new[] { "Admin" }, h["consultar_sin_roles"].Roles);
    }

    [Fact]
    public void Dos_GET_con_el_mismo_nombre_son_un_error()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GeneradorHerramientasOpenApi.Generar(new[] { ContratoEjemplo, ContratoEjemplo }));
    }

    [Fact]
    public void Con_los_contratos_reales_genera_herramientas_validas_y_sin_las_excluidas()
    {
        var carpeta = BuscarCarpetaDeContratos();
        var herramientas = CatalogoHerramientasGeneradas.CargarDeCarpeta(carpeta);

        Assert.NotEmpty(herramientas);
        Assert.Equal(herramientas.Count, herramientas.Select(h => h.Nombre).Distinct().Count());
        Assert.All(herramientas, h =>
        {
            Assert.Matches("^[a-z0-9_]+$", h.Nombre);
            Assert.InRange(h.Nombre.Length, 1, GeneradorHerramientasOpenApi.MaxLongitudNombre);
            Assert.StartsWith("/api/", h.Ruta);
            Assert.DoesNotContain(h.Ruta, GeneradorHerramientasOpenApi.Excluidos.Keys);
            Assert.NotEmpty(h.Roles);
        });
        Assert.DoesNotContain(herramientas, h => h.Ruta.Contains("magic-links"));
    }

    [Fact]
    public void Con_los_contratos_reales_un_Capataz_no_ve_lo_que_el_contrato_reserva_a_Compras()
    {
        var generadas = new HerramientasGeneradas(
            CatalogoHerramientasGeneradas.CargarDeCarpeta(BuscarCarpetaDeContratos()), new ClienteFalso());

        var capataz = generadas.Para("Capataz").Select(h => h.Ruta).ToList();
        var comprador = generadas.Para("Comprador").Select(h => h.Ruta).ToList();

        Assert.DoesNotContain("/api/usuarios/proveedores", capataz);
        Assert.Contains("/api/usuarios/proveedores", comprador);
        Assert.Empty(generadas.Para("Proveedor"));
        Assert.Empty(generadas.Para(null));
    }

    private static string BuscarCarpetaDeContratos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidata = Path.Combine(dir.FullName, "contracts", "openapi");
            if (Directory.Exists(candidata))
                return candidata;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró contracts/openapi subiendo desde " + AppContext.BaseDirectory);
    }

    // ── Definiciones para el modelo ────────────────────────────────────────────

    [Fact]
    public void La_definicion_sale_del_contrato_con_tipos_requeridos_y_tope_de_tamano()
    {
        var defs = Armar(out _).DefinicionesPara("Comprador");

        var listar = defs.Single(d => d!["function"]!["name"]!.GetValue<string>() == "consultar_ejemplos")!["function"]!;
        var props = listar["parameters"]!["properties"]!;
        Assert.Equal("integer", props["tamano"]!["type"]!.GetValue<string>());
        Assert.Equal(HerramientasGeneradas.TamanoMaximo, props["tamano"]!["maximum"]!.GetValue<int>()); // el contrato dice 50, pero acá se baja
        Assert.Equal(2, props["estado"]!["enum"]!.AsArray().Count);
        Assert.False(listar["parameters"]!["additionalProperties"]!.GetValue<bool>());

        var ver = defs.Single(d => d!["function"]!["name"]!.GetValue<string>() == "consultar_ejemplos_por_id")!["function"]!;
        Assert.Equal("id", ver["parameters"]!["required"]![0]!.GetValue<string>());
        Assert.Equal("uuid", ver["parameters"]!["properties"]!["id"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public void Cada_rol_recibe_solo_las_definiciones_que_el_contrato_le_permite()
    {
        var generadas = Armar(out _);

        var capataz = generadas.DefinicionesPara("Capataz").Select(d => d!["function"]!["name"]!.GetValue<string>()).ToList();

        Assert.Equal(new[] { "consultar_ejemplos" }, capataz);
    }

    // ── Ejecución ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Arma_la_ruta_y_la_consulta_con_los_valores_validados_y_escapados()
    {
        var generadas = Armar(out var cliente);

        var r = await generadas.EjecutarAsync("Comprador", "consultar_ejemplos_por_id", $"{{\"id\":\"{Uuid.ToUpperInvariant()}\"}}", default);

        Assert.True(r.Ok);
        Assert.Equal($"/api/ejemplos/{Uuid}", Assert.Single(cliente.Pedidos));
        Assert.Equal(7, r.Datos["total"]!.GetValue<int>());
        Assert.NotNull(r.Datos["datos"]);
    }

    [Fact]
    public async Task Si_el_modelo_no_pide_tamano_se_usa_el_chico_por_defecto()
    {
        var generadas = Armar(out var cliente);

        await generadas.EjecutarAsync("Comprador", "consultar_ejemplos", """{"estado":"Abierta","pagina":2}""", default);

        Assert.Equal("/api/ejemplos?estado=Abierta&pagina=2&tamano=10", Assert.Single(cliente.Pedidos));
    }

    [Theory]
    [InlineData("""{"id":"no-es-un-guid"}""")]
    [InlineData("""{"id":"../../admin"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","extra":1}""")]
    [InlineData("""no es json""")]
    public async Task Argumentos_malos_se_rechazan_sin_llamar_a_la_API(string argumentos)
    {
        var generadas = Armar(out var cliente);

        var r = await generadas.EjecutarAsync("Comprador", "consultar_ejemplos_por_id", argumentos, default);

        Assert.False(r.Ok);
        Assert.Equal("argumentos_invalidos", Error(r));
        Assert.Empty(cliente.Pedidos);
    }

    [Theory]
    [InlineData("""{"tamano":21}""")]
    [InlineData("""{"tamano":0}""")]
    [InlineData("""{"tamano":"5"}""")]
    [InlineData("""{"pagina":0}""")]
    [InlineData("""{"estado":"Inventada"}""")]
    [InlineData("""{"buscar":"a/b"}""")]
    [InlineData("""{"buscar":"x&admin=true"}""")]
    [InlineData("""{"buscar":".."}""")]
    public async Task Filtros_fuera_de_lo_que_dice_el_contrato_se_rechazan(string argumentos)
    {
        var generadas = Armar(out var cliente);

        var r = await generadas.EjecutarAsync("Comprador", "consultar_ejemplos", argumentos, default);

        Assert.Equal("argumentos_invalidos", Error(r));
        Assert.Empty(cliente.Pedidos);
    }

    [Fact]
    public async Task Un_rol_que_el_contrato_no_permite_no_ejecuta_aunque_el_modelo_la_pida()
    {
        var generadas = Armar(out var cliente);

        var r = await generadas.EjecutarAsync("Capataz", "consultar_ejemplos_por_id", $"{{\"id\":\"{Uuid}\"}}", default);

        Assert.Equal("herramienta_no_permitida", Error(r));
        Assert.Empty(cliente.Pedidos);
    }

    [Fact]
    public async Task Una_herramienta_que_no_existe_da_herramienta_desconocida()
    {
        var generadas = Armar(out _);

        var r = await generadas.EjecutarAsync("Admin", "borrar_todo", "{}", default);

        Assert.Equal("herramienta_desconocida", Error(r));
    }

    [Fact]
    public async Task Los_errores_de_la_API_llegan_como_codigos_estables()
    {
        var generadas = Armar(out var cliente);
        cliente.Respuesta = RespuestaApiLectura.Falla("sin_permiso");

        var r = await generadas.EjecutarAsync("Comprador", "consultar_ejemplos", "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("sin_permiso", Error(r));
    }

    // ── Dentro del agente ──────────────────────────────────────────────────────

    private sealed class ModeloQuePideYAnota : IModeloChat
    {
        private readonly Queue<RespuestaModelo> _respuestas;
        public List<string> Ofrecidas { get; } = new();
        public List<string> Resultados { get; } = new();

        public ModeloQuePideYAnota(params RespuestaModelo[] respuestas) => _respuestas = new(respuestas);

        public Task<RespuestaModelo> PedirAsync(JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
        {
            Ofrecidas.AddRange((herramientas ?? new JsonArray()).Select(d => d!["function"]!["name"]!.GetValue<string>()));
            var ultimo = mensajes[^1]!;
            if (ultimo["role"]?.GetValue<string>() == "tool")
                Resultados.Add(ultimo["content"]!.GetValue<string>());
            return Task.FromResult(_respuestas.Dequeue());
        }
    }

    private static Agente ArmarAgente(IModeloChat modelo, string rol, HerramientasGeneradas? generadas) => new(
        modelo,
        new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
        new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas()),
        new ContextoFalso { Rol = rol },
        null,
        new HerramientasRecepcion(new ConsultorRecepcionesFalso(), new ObrasFalsas()),
        new HerramientasLicitaciones(new ConsultorLicitacionesFalso(), new ObrasFalsas()),
        generadas);

    [Fact]
    public async Task El_agente_ofrece_y_ejecuta_las_generadas_de_su_rol()
    {
        var generadas = Armar(out var cliente);
        var modelo = new ModeloQuePideYAnota(
            Respuestas.PideHerramienta("{}", nombre: "consultar_ejemplos"),
            Respuestas.Final("Listo"));

        var r = await ArmarAgente(modelo, "Comprador", generadas).ResponderAsync("mostrame los ejemplos");

        Assert.True(r.Ok);
        Assert.Contains("consultar_ejemplos", modelo.Ofrecidas);
        Assert.Equal("/api/ejemplos?tamano=10", Assert.Single(cliente.Pedidos));
        Assert.Contains("\"ok\":true", Assert.Single(modelo.Resultados));
    }

    [Fact]
    public async Task Sin_generadas_el_agente_no_ofrece_ni_ejecuta_ninguna()
    {
        var modelo = new ModeloQuePideYAnota(
            Respuestas.PideHerramienta("{}", nombre: "consultar_ejemplos"),
            Respuestas.Final("No pude"));

        var r = await ArmarAgente(modelo, "Comprador", null).ResponderAsync("mostrame los ejemplos");

        Assert.DoesNotContain("consultar_ejemplos", modelo.Ofrecidas);
        Assert.Contains("herramienta_desconocida", Assert.Single(modelo.Resultados));
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task Un_catalogo_vacio_deja_las_herramientas_del_perfil_como_estaban()
    {
        var vacio = new HerramientasGeneradas(Array.Empty<HerramientaGenerada>(), new ClienteFalso());
        var conVacio = new ModeloQuePideYAnota(Respuestas.Final("Hola"));
        var sin = new ModeloQuePideYAnota(Respuestas.Final("Hola"));

        await ArmarAgente(conVacio, "Admin", vacio).ResponderAsync("hola");
        await ArmarAgente(sin, "Admin", null).ResponderAsync("hola");

        Assert.Equal(sin.Ofrecidas.OrderBy(x => x), conVacio.Ofrecidas.OrderBy(x => x));
    }
}
