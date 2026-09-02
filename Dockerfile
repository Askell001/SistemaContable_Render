# =========================================================================
# DOCKERFILE PARA SISTEMA CONTABLE ASP.NET MVC (.NET Framework 4.8 / Mono)
# Optimizado para Render.com con MONO_PATH y pre-registro GAC de Razor
# =========================================================================

FROM ubuntu:20.04

ENV DEBIAN_FRONTEND=noninteractive
ENV MONO_PATH=/app/bin:/usr/lib/mono/4.5

# 1. Instalar Mono Complete, MSBuild, NuGet y XSP4 mediante clave GPG directa por HTTPS
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        gnupg \
        ca-certificates \
        curl \
    && curl -fsSL https://download.mono-project.com/repo/xamarin.gpg | gpg --dearmor -o /etc/apt/trusted.gpg.d/mono-official.gpg \
    && echo "deb https://download.mono-project.com/repo/ubuntu stable-focal main" > /etc/apt/sources.list.d/mono-official.list \
    && apt-get update && \
    apt-get install -y --no-install-recommends \
        mono-complete \
        mono-xsp4 \
        msbuild \
        nuget \
        ca-certificates-mono \
    && rm -rf /var/lib/apt/lists/*

# 2. Directorio de trabajo
WORKDIR /app

# 3. Copiar archivos de solución y paquetes para optimizar caché de capas
COPY SistemaContable.sln ./
COPY SistemaContable.csproj ./
COPY packages.config ./

# 4. Restaurar paquetes NuGet
RUN nuget restore SistemaContable.sln

# 5. Copiar el resto del código fuente
COPY . ./

# 6. Compilar en modo Release
RUN msbuild /p:Configuration=Release /p:Platform="Any CPU" /v:m SistemaContable.sln

# 7. Poblar /app/bin con todas las DLLs de dependencias y registrar en GAC
RUN mkdir -p /app/bin && \
    find /app/packages -name "*.dll" ! -path "*/analyzers/*" -exec cp -n {} /app/bin/ \; && \
    find /app/bin -name "*SourceGeneration*.dll" -o -name "*CodeAnalysis*.dll" -o -name "*Analyzer*.dll" | xargs -r rm -f && \
    gacutil -i /app/bin/System.Web.WebPages.Razor.dll || true && \
    gacutil -i /app/bin/System.Web.Razor.dll || true && \
    gacutil -i /app/bin/System.Web.WebPages.dll || true && \
    gacutil -i /app/bin/System.Web.Mvc.dll || true && \
    gacutil -i /app/bin/System.Web.Helpers.dll || true && \
    gacutil -i /app/bin/System.Web.WebPages.Deployment.dll || true && \
    gacutil -i /app/bin/Microsoft.Web.Infrastructure.dll || true

# 8. Crear y asegurar permisos totales de lectura y escritura en App_Data/Respaldos
RUN mkdir -p /app/App_Data/Respaldos && \
    chmod -R 777 /app/App_Data

# 9. Render inyecta dinámicamente la variable $PORT (por defecto 10000)
ENV PORT=10000
EXPOSE 10000

# 10. Script de arranque con MONO_PATH explícito y puerto dinámico
RUN printf '#!/bin/bash\nset -e\nexport MONO_PATH="/app/bin:/usr/lib/mono/4.5:${MONO_PATH:-}"\nAPP_PORT="${PORT:-10000}"\nmkdir -p /app/App_Data/Respaldos\nchmod -R 777 /app/App_Data\necho "=== Sistema Contable iniciado en puerto $APP_PORT (MONO_PATH: $MONO_PATH) ==="\nexec xsp4 --port "$APP_PORT" --address 0.0.0.0 --nonstop --root /app\n' > /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh

# 11. Punto de entrada
ENTRYPOINT ["/bin/bash", "/app/entrypoint.sh"]
