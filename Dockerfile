FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar primero (capa cacheable): solo los .csproj
COPY Tahona.BakeryPOS.sln ./
COPY src/Tahona.BakeryPOS.Api/Tahona.BakeryPOS.Api.csproj src/Tahona.BakeryPOS.Api/
COPY src/Tahona.BakeryPOS.Application/Tahona.BakeryPOS.Application.csproj src/Tahona.BakeryPOS.Application/
COPY src/Tahona.BakeryPOS.Domain/Tahona.BakeryPOS.Domain.csproj src/Tahona.BakeryPOS.Domain/
COPY src/Tahona.BakeryPOS.Infrastructure/Tahona.BakeryPOS.Infrastructure.csproj src/Tahona.BakeryPOS.Infrastructure/
COPY src/Tahona.BakeryPOS.SharedKernel/Tahona.BakeryPOS.SharedKernel.csproj src/Tahona.BakeryPOS.SharedKernel/
RUN dotnet restore src/Tahona.BakeryPOS.Api/Tahona.BakeryPOS.Api.csproj

COPY src/ src/
RUN dotnet publish src/Tahona.BakeryPOS.Api/Tahona.BakeryPOS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Llaves de Data Protection (DataProtection__KeysPath=/keys): el volumen nombrado hereda este dueño.
RUN mkdir -p /keys && chown $APP_UID /keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tahona.BakeryPOS.Api.dll"]
