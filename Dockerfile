FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY src/VSMS.Core/*.csproj src/VSMS.Core/
COPY src/VSMS.Infrastructure/*.csproj src/VSMS.Infrastructure/
COPY src/VSMS.Jobs/*.csproj src/VSMS.Jobs/
COPY src/VSMS.Web/*.csproj src/VSMS.Web/

# Restore dependencies
RUN dotnet restore src/VSMS.Web/VSMS.Web.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish src/VSMS.Web/VSMS.Web.csproj -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

# Railway uses PORT env var
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

ENTRYPOINT ["dotnet", "VSMS.Web.dll"]
