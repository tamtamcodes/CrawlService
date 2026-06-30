# Stage 1: Build the C# application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore
COPY ["SocialCrawler.csproj", "./"]
RUN dotnet restore "SocialCrawler.csproj"

# Copy the rest of the source code and publish
COPY . .
RUN dotnet publish "SocialCrawler.csproj" -c Release -o /app/publish

# Stage 2: Final runtime container
FROM mcr.microsoft.com/playwright/dotnet:v1.61.0-noble AS final
WORKDIR /app

# 1. Install .NET 10.0 ASP.NET Core runtime (the Playwright dotnet base image comes with .NET 8)
RUN curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet

# 2. Install Python3 and pip to download CloakBrowser
RUN apt-get update && apt-get install -y python3 python3-pip python3-venv && rm -rf /var/lib/apt/lists/*

# 3. Create a python virtual environment, install cloakbrowser, and download the binary
RUN python3 -m venv /opt/cloakbrowser-venv && \
    /opt/cloakbrowser-venv/bin/pip install --upgrade pip && \
    /opt/cloakbrowser-venv/bin/pip install cloakbrowser && \
    /opt/cloakbrowser-venv/bin/python3 -c "from cloakbrowser.download import ensure_binary; ensure_binary()"

# 4. Copy the downloaded CloakBrowser binary and its environment assets to /app/cloakbrowser
RUN mkdir -p /app/cloakbrowser && \
    cp -r /root/.cloakbrowser/chromium-*/* /app/cloakbrowser/

# Set Environment variables
ENV PORT=5000
ENV BROWSER_EXECUTABLE_PATH=/app/cloakbrowser/chrome
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Copy C# published application files
COPY --from=build /app/publish .

# Run the app
ENTRYPOINT ["dotnet", "SocialCrawler.dll"]
