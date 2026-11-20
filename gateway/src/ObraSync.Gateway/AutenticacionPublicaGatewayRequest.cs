namespace ObraSync.Gateway;

/// <summary>Reconoce los POST públicos de Usuarios que no llevan token y por eso se limitan por IP.</summary>
public static class AutenticacionPublicaGatewayRequest
{
    private static readonly string[] Rutas =
    {
        "/api/usuarios/login",
        "/api/usuarios/registro",
        "/api/usuarios/password/recuperar",
        "/api/usuarios/password/restablecer"
    };

    public static bool Es(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)) return false;
        var path = request.Path.Value?.TrimEnd('/');
        return path is not null && Rutas.Contains(path, StringComparer.OrdinalIgnoreCase);
    }
}
