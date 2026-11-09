# syntax=docker/dockerfile:1
# Build context: ./api   →   docker build -t annaba-digital-api ./api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AnnabaDigital.sln ./
COPY src/AnnabaDigital.Domain/AnnabaDigital.Domain.csproj src/AnnabaDigital.Domain/
COPY src/AnnabaDigital.Application/AnnabaDigital.Application.csproj src/AnnabaDigital.Application/
COPY src/AnnabaDigital.Infrastructure/AnnabaDigital.Infrastructure.csproj src/AnnabaDigital.Infrastructure/
COPY src/AnnabaDigital.Api/AnnabaDigital.Api.csproj src/AnnabaDigital.Api/
RUN dotnet restore src/AnnabaDigital.Api/AnnabaDigital.Api.csproj
COPY src ./src
RUN dotnet publish src/AnnabaDigital.Api/AnnabaDigital.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS run
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
# /health is served by the API itself and never goes through the UI proxy.
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 \
  CMD ["dotnet", "AnnabaDigital.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "AnnabaDigital.Api.dll"]
