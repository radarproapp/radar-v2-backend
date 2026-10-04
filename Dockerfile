FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
# API-only host: the React SPA is built and deployed separately (Vercel/Netlify) and
# reaches this container cross-origin, so nothing from web/dist is copied in here.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 80

# Railway (and similar PaaS hosts) inject a PORT env var the container must
# listen on; fall back to 80 for hosts that don't (e.g. Azure App Service).
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet RadarV2.dll --urls http://+:${PORT:-80}"]
