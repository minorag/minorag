# ---------- build frontend ----------
FROM node:20-alpine AS ui-build
WORKDIR /ui

# Cache deps
COPY webview/minorag-ui/package*.json ./
RUN npm ci

# Copy sources + build
COPY webview/minorag-ui/ ./
RUN npm run build -- --configuration production --output-path=dist/wwwroot


# ---------- build backend ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + projects first (better layer caching)
COPY src/Minorag.sln ./src/Minorag.sln
COPY src/Minorag.Core/Minorag.Core.csproj ./src/Minorag.Core/
COPY src/Minorag.Api/Minorag.Api.csproj ./src/Minorag.Api/

# Restore
RUN dotnet restore ./src/Minorag.Api/Minorag.Api.csproj

# Copy everything else
COPY . .

# Publish
RUN dotnet publish ./src/Minorag.Api/Minorag.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Copy built frontend into API wwwroot
COPY --from=ui-build /ui/dist/wwwroot/browser /app/publish/wwwroot/


# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:7777
EXPOSE 7777

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Minorag.Api.dll"]