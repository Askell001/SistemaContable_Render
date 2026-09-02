# =========================================================================
# DOCKERFILE PARA SISTEMA CONTABLE ASP.NET MVC (.NET Framework 4.8 / Mono)
# Optimizado para Render.com con Ubuntu 20.04 LTS y copia total de librerías
# =========================================================================

FROM ubuntu:20.04

ENV DEBIAN_FRONTEND=noninteractive

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

# 6. Compilar en modo Release y copiar todas las DLLs de ejecución desde /lib/ hacia /app/bin
RUN msbuild /p:Configuration=Release /p:Platform="Any CPU" /v:m SistemaContable.sln && \
    find /app/packages -path "*/lib/*" -name "*.dll" -exec cp -n {} /app/bin/ \; && \
    find /app/bin -name "*SourceGeneration*.dll" -o -name "*CodeAnalysis*.dll" -o -name "*Analyzer*.dll" | xargs -r rm -f

# 7. Crear y asegurar permisos totales de lectura y escritura en App_Data/Respaldos
RUN mkdir -p /app/App_Data/Respaldos && \
    chmod -R 777 /app/App_Data

# 8. Render inyecta dinámicamente la variable $PORT (por defecto 10000)
ENV PORT=10000
EXPOSE 10000

# 9. Script de arranque resiliente con soporte de puerto dinámico y permisos
RUN printf '#!/bin/bash\nset -e\nAPP_PORT="${PORT:-10000}"\nmkdir -p /app/App_Data/Respaldos\nchmod -R 777 /app/App_Data\necho "=== Sistema Contable iniciado en puerto $APP_PORT ==="\nexec xsp4 --port "$APP_PORT" --address 0.0.0.0 --nonstop --root /app\n' > /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh

# 10. Punto de entrada
ENTRYPOINT ["/bin/bash", "/app/entrypoint.sh"]
