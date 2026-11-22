using Microsoft.AspNetCore.Http;

namespace ObraSync.Gateway;

/// <summary>
/// Pedidos al hub de tiempo real del chat por orden (#218, #225): negociación, WebSocket, Server-Sent Events
/// y Long Polling. Como el navegador no puede mandar encabezados en un WebSocket, el JWT viaja en la URL
/// (?access_token=...); por eso el Gateway no escribe esa URL en sus registros.
/// </summary>
public static class ChatHubGatewayRequest
{
    public const string Ruta = "/api/ordenes-compra/chat-hub";

    public static bool EsChatHub(HttpRequest request)
    {
        var path = request.Path.Value;
        return path is not null
            && path.StartsWith(Ruta, StringComparison.OrdinalIgnoreCase)
            && (path.Length == Ruta.Length || path[Ruta.Length] == '/');
    }
}
