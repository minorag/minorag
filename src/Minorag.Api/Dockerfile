# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + projects first (better layer caching)
COPY src/Minorag.sln ./src/Minorag.sln
COPY src/Minorag.Core/Minorag.Core.csproj ./src/Minorag.Core/
COPY src/Minorag.Api/Minorag.Api.csproj ./src/Minorag.Api/

# Restore
RUN dotnet restore ./src/Minorag.Api/Minorag.Api.csproj

# Copy the rest
COPY . .

# Publish
RUN dotnet publish ./src/Minorag.Api/Minorag.Api.csproj -c Release -o /app/publish /p:UseAppHost=false


# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Bind to all interfaces inside container
ENV ASPNETCORE_URLS=http://0.0.0.0:7777
EXPOSE 7777

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Minorag.Api.dll"]