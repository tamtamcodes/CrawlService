# Stage 1: Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["SocialCrawler.csproj", "./"]
RUN dotnet restore "SocialCrawler.csproj"

# Copy the rest of the source code
COPY . .
RUN dotnet build "SocialCrawler.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "SocialCrawler.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime stage
# Official Playwright .NET Noble runtime image containing all Chromium dependencies
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS final
WORKDIR /app

# Ensure standard environment configurations for production/Railway
ENV PORT=8080
ENV HEADLESS=true
ENV CLOAKBROWSER_CACHE_DIR=/app/.cloakbrowser
ENV BROWSER_EXECUTABLE_PATH=""

# Expose port
EXPOSE 8080

# Copy published outputs
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "SocialCrawler.dll"]