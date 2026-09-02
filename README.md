# Sistema Contable - ASP.NET MVC (.NET Framework 4.8 + MongoDB)

Solución arquitectónica base para el Sistema Contable desarrollada en **ASP.NET MVC 5** sobre **.NET Framework 4.8**, integrada con **MongoDB Driver (v2.25+)** y soporte para **BCrypt.Net-Next**.

---

## 📁 Estructura del Proyecto

```
sistema contable/
├── App_Start/
│   ├── BundleConfig.cs
│   ├── FilterConfig.cs
│   └── RouteConfig.cs
├── Controllers/
│   └── HomeController.cs               # Panel de diagnóstico y monitoreo de MongoDB
├── Data/
│   └── MongoDbContext.cs               # Singleton Thread-Safe con Ping y Resiliencia
├── Models/
│   ├── AsientoContable.cs              # Modelo de Asientos y DetalleAsiento (Partida doble)
│   ├── CuentaContable.cs               # Catálogo de Cuentas Contables
│   ├── Notificacion.cs                 # Notificaciones del Sistema
│   ├── Rol.cs                          # Roles y Permisos
│   └── Usuario.cs                      # Usuarios con PasswordHash
├── Properties/
│   └── AssemblyInfo.cs
├── Views/
│   ├── Home/
│   │   └── Index.cshtml                # Dashboard con métricas de conexión en vivo
│   ├── Shared/
│   │   └── _Layout.cshtml              # Master layout con Bootstrap 5
│   ├── _ViewStart.cshtml
│   └── Web.config
├── Global.asax / Global.asax.cs
├── packages.config                     # Dependencias NuGet (MongoDB.Driver, BCrypt, etc.)
├── SistemaContable.csproj              # Proyecto VS con soporte IIS Express
├── SistemaContable.sln                 # Solución de Visual Studio
└── Web.config                          # Conexiones MongoDB y configuración activa
```

---

## ⚙️ Configuración en `Web.config`

### 1. Cadenas de Conexión
```xml
<connectionStrings>
  <!-- Conexión Local -->
  <add name="MongoLocal" connectionString="mongodb://localhost:27017" />
  
  <!-- Conexión en la Nube (MongoDB Atlas) -->
  <add name="MongoAtlas" connectionString="mongodb+srv://user:12345@registrousuarios.e6jeny6.mongodb.net/" />
</connectionStrings>
```

### 2. Selector de Conexión Activa
Para alternar entre entornos (Local / Atlas), cambia la clave `ActiveMongoConnection`:
```xml
<appSettings>
  <!-- Cambia a 'MongoLocal' o 'MongoAtlas' según el entorno requerido -->
  <add key="ActiveMongoConnection" value="MongoAtlas" />
  <add key="MongoDatabaseName" value="ContabilidadDB" />
</appSettings>
```

---

## 🛡️ Contexto de Base de Datos (`MongoDbContext.cs`)

- **Patrón Singleton Thread-Safe**: Usa `Lazy<MongoDbContext>` para garantizar una única instancia de `IMongoClient` en toda la aplicación (recomendado por las mejores prácticas del MongoDB C# Driver).
- **Manejo de Excepciones**: No bloquea ni colapsa la aplicación si el clúster está temporalmente inactivo.
- **Diagnóstico y Logging**: Emite logs vía `System.Diagnostics.Trace` y `Debug`.
- **Colecciones Tipadas Disponibles**:
  - `MongoDbContext.Instance.Usuarios`
  - `MongoDbContext.Instance.Roles`
  - `MongoDbContext.Instance.CuentasContables`
  - `MongoDbContext.Instance.AsientosContables`
  - `MongoDbContext.Instance.Notificaciones`
- **Health Check**: Método `TestConnection()` para ejecutar `ping` en tiempo real.

---

## 🚀 Cómo Ejecutar en Visual Studio (F5)

1. Abre [`SistemaContable.sln`](file:///c:/Users/PC/Desktop/sistema%20contable/SistemaContable.sln) en **Visual Studio 2019 o 2022**.
2. Haz clic derecho en la solución -> **Restaurar paquetes NuGet** (o compila directamente y VS los restaurará automáticamente).
3. Presiona **F5** para iniciar en IIS Express.
4. El navegador se abrirá en `http://localhost:51234/` mostrando el panel de diagnóstico de MongoDB.
