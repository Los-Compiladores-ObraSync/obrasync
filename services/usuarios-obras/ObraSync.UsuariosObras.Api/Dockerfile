# --- ETAPA 1: Compilar la aplicación ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiamos todo el contenido de la API al contenedor
COPY . .

# Restauramos y publicamos directamente el proyecto
RUN dotnet restore "ObraSync.UsuariosObras.Api.csproj"
RUN dotnet publish "ObraSync.UsuariosObras.Api.csproj" -c Release -o /app/publish

# --- ETAPA 2: Ejecutar la aplicación en el servidor del contenedor ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "ObraSync.UsuariosObras.Api.dll"]