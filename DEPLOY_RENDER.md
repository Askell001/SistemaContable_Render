# Guía Rápida: Despliegue en GitHub y Render.com

Este directorio contiene el proyecto **SistemaContable** listo para subir a GitHub y desplegarse en **Render.com** mediante Docker.

---

## 1. Inicializar repositorio y subir a GitHub

Abre una terminal (PowerShell o Git Bash) en esta carpeta (`C:\Users\PC\Desktop\SistemaContable_Render`):

```bash
git init
git add .
git commit -m "Initial commit - Sistema Contable listo para Render"
git branch -M main
git remote add origin https://github.com/TU_USUARIO/TU_REPOSITORIO.git
git push -u origin main
```

---

## 2. Desplegar en Render.com

1. Ingresa a [https://dashboard.render.com/](https://dashboard.render.com/)
2. Haz clic en el botón **New +** y selecciona **Web Service**.
3. Selecciona tu repositorio de GitHub recién creado.
4. Completa la configuración básica:
   - **Name:** `sistema-contable` (o el nombre que prefieras).
   - **Language / Environment:** `Docker` (Render lo detectará automáticamente).
   - **Region:** Selecciona la región que prefieras (ej. *Oregon (US West)* u *Ohio (US East)*).
   - **Branch:** `main`
   - **Plan:** `Free` o `Starter`.
5. Haz clic en **Create Web Service**.

---

## 3. Características incluidas en este paquete

- **Dockerfile con Mono 6.12 y XSP4:** Ejecuta ASP.NET MVC (.NET Framework 4.8) en Linux de forma estable.
- **Detección Dinámica de Puerto:** Render inyecta la variable `$PORT` y el contenedor se enlaza automáticamente a ese puerto.
- **Permisos Totales en `App_Data/Respaldos`:** El contenedor crea y asigna `chmod -R 777 /app/App_Data` para permitir guardar el respaldo `Respaldo_Contabilidad_Full.json` sin errores.
- **Conectividad a MongoDB Atlas:** Certificados SSL incluidos para conexión segura a la nube.
