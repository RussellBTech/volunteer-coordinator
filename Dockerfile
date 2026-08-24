# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /source
COPY VolunteerCoordinator.sln ./
COPY src/VolunteerCoordinator.Domain/VolunteerCoordinator.Domain.csproj src/VolunteerCoordinator.Domain/
COPY src/VolunteerCoordinator.Application/VolunteerCoordinator.Application.csproj src/VolunteerCoordinator.Application/
COPY src/VolunteerCoordinator.Infrastructure/VolunteerCoordinator.Infrastructure.csproj src/VolunteerCoordinator.Infrastructure/
COPY src/VolunteerCoordinator.Web/VolunteerCoordinator.Web.csproj src/VolunteerCoordinator.Web/
RUN dotnet restore src/VolunteerCoordinator.Web/VolunteerCoordinator.Web.csproj

FROM restore AS build
COPY src/ src/
RUN dotnet publish src/VolunteerCoordinator.Web/VolunteerCoordinator.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "VolunteerCoordinator.Web.dll"]
