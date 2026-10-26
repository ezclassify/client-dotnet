# Build-only native tooling. SDK lifecycle commands remain in the Makefile.
FROM node:24.22.0-bookworm-slim@sha256:a5f4366e834695d5f363b920c104953a535d3a9864d1bdf74566a0ebe01daee5 AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
COPY --from=node /usr/local/ /usr/local/
# Make owns the lifecycle; Python runs the repository's notices gate.
RUN apt-get update && apt-get upgrade -y && apt-get install -y --no-install-recommends make git python3 && rm -rf /var/lib/apt/lists/*
# The SDK lifecycle uses dotnet and Make; it does not invoke PowerShell.
RUN rm -rf /usr/share/powershell /usr/bin/pwsh && npm install --global npm@11.21.0 --ignore-scripts \
    && npm --prefix /usr/local/lib/node_modules/npm pkg delete devDependencies \
    && npm --prefix /usr/local/lib/node_modules/npm update --omit=dev --ignore-scripts \
         --before="$(node -p 'new Date(Date.now() - 7 * 86400000).toISOString()')"
ENV NODE_OPTIONS=--use-openssl-ca
RUN node --version && npm --version && dotnet --version && make --version && git --version && python3 --version
