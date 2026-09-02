# =========================================================================
# DOCKERFILE PARA SISTEMA CONTABLE ASP.NET MVC (.NET Framework 4.8 / Mono)
# Compatible con Render.com y despliegues Linux Docker en la nube
# =========================================================================

FROM mono:6.12

# Instalar Mono XSP4 (servidor web ASP.NET para Linux), certificados SSL y herramientas
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        mono-xsp4 \
        ca-certificates-mono \
        curl \
    && rm -rf /var/lib/apt/lists/*

# Establecer directorio de la aplicación
WORKDIR /app

# Copiar archivos de solución y dependencias primero para optimizar la caché
COPY SistemaContable.sln ./
COPY SistemaContable.csproj ./
COPY packages.config ./

# Restaurar paquetes NuGet
RUN nuget restore SistemaContable.sln

# Copiar el código fuente completo
COPY . ./

# Compilar la aplicación en Release
RUN msbuild /p:Configuration=Release /p:Platform="Any CPU" /v:m SistemaContable.sln

# Crear y asegurar permisos totales de lectura y escritura en App_Data y App_Data/Respaldos
RUN mkdir -p /app/App_Data/Respaldos && \
    chmod -R 777 /app/App_Data

# Render inyecta la variable de entorno $PORT dinámicamente (por defecto 10000)
ENV PORT=10000
EXPOSE 10000

# Crear script de arranque con soporte de puerto dinámico y permisos
RUN printf '#!/bin/bash\nset -e\nAPP_PORT="${PORT:-10000}"\nmkdir -p /app/App_Data/Respaldos\nchmod -R 777 /app/App_Data\necho "=== Sistema Contable iniciado en puerto $APP_PORT ==="\nexec xsp4 --port "$APP_PORT" --address 0.0.0.0 --nonstop --root /app\n' > /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh

# Ejecutar la aplicación
ENTRYPOINT ["/bin/bash", "/app/entrypoint.sh"]
