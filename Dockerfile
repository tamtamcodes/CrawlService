# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: Build the C# application
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SocialCrawler.csproj", "./"]
RUN dotnet restore "SocialCrawler.csproj"
COPY . .
RUN dotnet publish "SocialCrawler.csproj" -c Release -o /app/publish

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: Final runtime image
# Use the official Playwright .NET Noble image (Ubuntu 24.04) as base.
# It already has Chromium, all system deps (libgbm, libnss3 etc.) pre-installed.
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/playwright/dotnet:v1.61.0-noble AS final
WORKDIR /app

# ── 1. Install .NET 10 ASP.NET Core runtime ──────────────────────────────────
# The Playwright/dotnet base image ships with .NET 8; project targets .NET 10.
RUN apt-get update && apt-get install -y curl ca-certificates && rm -rf /var/lib/apt/lists/*
RUN curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin \
      --channel 10.0 \
      --runtime aspnetcore \
      --install-dir /usr/share/dotnet

# ── 2. Install Python + pip for the CloakBrowser downloader ──────────────────
RUN apt-get update && \
    apt-get install -y python3 python3-pip python3-venv && \
    rm -rf /var/lib/apt/lists/*

# ── 3. Download the CloakBrowser stealth Chromium binary ─────────────────────
# Create a venv to avoid PEP-668 "externally-managed-environment" errors on
# Ubuntu 24.04 (noble).
RUN python3 -m venv /opt/cb-venv && \
    /opt/cb-venv/bin/pip install --upgrade pip --quiet && \
    /opt/cb-venv/bin/pip install cloakbrowser --quiet

# Trigger binary download at build time so the container doesn't try to
# phone home at startup.
RUN /opt/cb-venv/bin/python3 -c "from cloakbrowser.download import ensure_binary; p = ensure_binary(); print('[Dockerfile] CloakBrowser binary:', p)"

# ── 4. Copy & expose the binary ──────────────────────────────────────────────
# The binary lands in /root/.cloakbrowser/chromium-<version>/chrome.
# We copy the entire versioned directory into a stable path so the env var
# never needs to change.
RUN CB_DIR=$(find /root/.cloakbrowser -maxdepth 1 -type d -name 'chromium-*' | head -1) && \
    echo "[Dockerfile] CloakBrowser dir: $CB_DIR" && \
    cp -r "$CB_DIR" /app/cloakbrowser-bin && \
    ln -s /app/cloakbrowser-bin/chrome /app/cloakbrowser

# ── 5. Environment variables ─────────────────────────────────────────────────
# PORT: Render sets this automatically; default to 5000 for local dev.
ENV PORT=5000
# Tell our BrowserLauncher to use the stealth Chromium instead of the standard one.
ENV BROWSER_EXECUTABLE_PATH=/app/cloakbrowser
# Keep Playwright from downloading its own Chromium (we won't use it).
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
# Always headless on Render (no display server available).
ENV HEADLESS=true

# ── 6. Copy published .NET application ───────────────────────────────────────
COPY --from=build /app/publish .

# ── 7. Entrypoint ────────────────────────────────────────────────────────────
ENTRYPOINT ["dotnet", "SocialCrawler.dll"]
