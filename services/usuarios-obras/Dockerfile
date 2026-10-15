# --- ETAPA 1: Compilar la aplicación ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj de todas las capas
COPY ["ObraSync.Usuarios.Api/ObraSync.Usuarios.Api.csproj", "ObraSync.Usuarios.Api/"]
COPY ["ObraSync.Usuarios.Application/ObraSync.Usuarios.Application.csproj", "ObraSync.Usuarios.Application/"]
COPY ["ObraSync.Usuarios.Domain/ObraSync.Usuarios.Domain.csproj", "ObraSync.Usuarios.Domain/"]
COPY ["ObraSync.Usuarios.Infrastructure/ObraSync.Usuarios.Infrastructure.csproj", "ObraSync.Usuarios.Infrastructure/"]

# Restaurar paquetes NuGet
RUN dotnet restore "ObraSync.Usuarios.Api/ObraSync.Usuarios.Api.csproj"

# Copiar todo el código fuente
COPY . .

# Compilar y publicar la API
RUN dotnet publish "ObraSync.Usuarios.Api/ObraSync.Usuarios.Api.csproj" -c Release -o /app/publish

# --- ETAPA 2: Servidor de ejecución ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "ObraSync.Usuarios.Api.dll"]
