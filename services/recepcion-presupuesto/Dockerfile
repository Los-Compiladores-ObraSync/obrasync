# ==============================================================================
# Etapa 1: Runtime base liviano y seguro (ASP.NET Core 8.0)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# ==============================================================================
# Etapa 2: Entorno de compilación y restauración con SDK de .NET 8
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copiamos primero los archivos de proyecto (.csproj) para aprovechar el cache
# de capas de Docker. Si el código cambia pero las dependencias no, no restaura de nuevo.
COPY ["ObraSync.Recepcion.Domain/ObraSync.Recepcion.Domain.csproj", "ObraSync.Recepcion.Domain/"]
COPY ["ObraSync.Recepcion.Application/ObraSync.Recepcion.Application.csproj", "ObraSync.Recepcion.Application/"]
COPY ["ObraSync.Recepcion.Infrastructure/ObraSync.Recepcion.Infrastructure.csproj", "ObraSync.Recepcion.Infrastructure/"]
COPY ["ObraSync.Recepcion.Api/ObraSync.Recepcion.Api.csproj", "ObraSync.Recepcion.Api/"]

RUN dotnet restore "ObraSync.Recepcion.Api/ObraSync.Recepcion.Api.csproj"

# Copiamos todo el código fuente del microservicio y compilamos
COPY . .
WORKDIR "/src/ObraSync.Recepcion.Api"
RUN dotnet build "ObraSync.Recepcion.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ==============================================================================
# Etapa 3: Publicación optimizada (Release sin AppHost innecesario)
# ==============================================================================
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ObraSync.Recepcion.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Etapa 4: Imagen final productiva
# ==============================================================================
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObraSync.Recepcion.Api.dll"]
