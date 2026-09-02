using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using SistemaContable.Data;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio de Sincronización Avanzada y Restauración de Datos (Auto-Healing Sync).
    /// Compara marcas de tiempo (Ecuador UTC-5) entre Atlas, Localhost y el archivo JSON del Escritorio,
    /// selecciona la fuente con los datos más recientes y replica la información de forma resiliente.
    /// </summary>
    public class SyncService
    {
        private const string LocalConnectionString = "mongodb://localhost:27017";
        private const string PrimaryDatabaseName = "ContabilidadDB";
        private const string BackupDatabaseName = "ContabilidadDB_Backup";

        public static DateTime ObtenerHoraEcuador()
        {
            try
            {
                var tzEcuador = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzEcuador);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(-5);
            }
        }

        /// <summary>
        /// Valida si el proceso de IIS tiene permisos efectivos de escritura en la carpeta.
        /// </summary>
        private static bool ProbarEscrituraEnCarpeta(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return false;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                string testFile = Path.Combine(folder, ".test_write_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtiene o crea la ruta física de la carpeta de respaldos en el servidor AWS (~/App_Data/Respaldos/).
        /// Incluye fallback automático a directorios de sistema si IIS tiene permisos restringidos.
        /// </summary>
        public static string ObtenerRutaCarpetaRespaldos()
        {
            var candidatos = new List<string>();

            // 1. App_Data en contexto web IIS (Ej: C:\inetpub\SistemaContablePublico\App_Data\Respaldos)
            try
            {
                string mapPath = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/Respaldos/");
                if (!string.IsNullOrEmpty(mapPath)) candidatos.Add(mapPath);
            }
            catch { }

            // 2. App_Data en contexto BaseDirectory
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir)) candidatos.Add(Path.Combine(baseDir, "App_Data", "Respaldos"));
            }
            catch { }

            // 3. Fallbacks seguros del sistema en AWS EC2 (ProgramData y Temp)
            try
            {
                string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrEmpty(commonData)) candidatos.Add(Path.Combine(commonData, "SistemaContable", "Respaldos"));
            }
            catch { }

            try
            {
                string tempPath = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tempPath)) candidatos.Add(Path.Combine(tempPath, "SistemaContable", "Respaldos"));
            }
            catch { }

            foreach (var dir in candidatos)
            {
                if (ProbarEscrituraEnCarpeta(dir))
                {
                    return dir;
                }
            }

            return candidatos.Count > 0 ? candidatos[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "Respaldos");
        }

        /// <summary>
        /// Obtiene todas las rutas potenciales donde puede residir el archivo de respaldo para su lectura.
        /// </summary>
        public static List<string> ObtenerRutasPosiblesJson()
        {
            var rutas = new List<string>();

            try
            {
                string mapPath = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/Respaldos/Respaldo_Contabilidad_Full.json");
                if (!string.IsNullOrEmpty(mapPath)) rutas.Add(mapPath);
            }
            catch { }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir)) rutas.Add(Path.Combine(baseDir, "App_Data", "Respaldos", "Respaldo_Contabilidad_Full.json"));
            }
            catch { }

            try
            {
                string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrEmpty(commonData)) rutas.Add(Path.Combine(commonData, "SistemaContable", "Respaldos", "Respaldo_Contabilidad_Full.json"));
            }
            catch { }

            try
            {
                string tempPath = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tempPath)) rutas.Add(Path.Combine(tempPath, "SistemaContable", "Respaldos", "Respaldo_Contabilidad_Full.json"));
            }
            catch { }

            return rutas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Obtiene la ruta física del archivo Respaldo_Contabilidad_Full.json en el servidor AWS.
        /// </summary>
        public static string ObtenerRutaArchivoJson()
        {
            string folderPath = ObtenerRutaCarpetaRespaldos();
            return Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");
        }

        /// <summary>
        /// Ejecuta el proceso integral de Auto-Healing Sync:
        /// 1. Lectura de marcas de tiempo.
        /// 2. Selección de la Fuente Válida más reciente.
        /// 3. Propagación bidireccional / réplica resiliente.
        /// </summary>
        public async Task<ResultadoSync> SincronizarYRestaurarAsync()
        {
            var horaEjecucionEC = ObtenerHoraEcuador();
            var resultado = new ResultadoSync
            {
                FechaEjecucionEC = horaEjecucionEC,
                Success = false
            };

            var dbContext = MongoDbContext.Instance;

            DateTime? fechaAtlas = null;
            DateTime? fechaLocal = null;
            DateTime? fechaJson = null;

            BackupCompletoDTO dataAtlas = null;
            BackupCompletoDTO dataLocal = null;
            BackupCompletoDTO dataJson = null;

            bool atlasDisponible = false;
            bool localDisponible = false;
            bool jsonDisponible = false;

            // =========================================================================
            // PASO 1: Lectura de marcas de tiempo y datos desde las 3 fuentes
            // =========================================================================

            // 1.1 LECTURA ATLAS
            try
            {
                if (dbContext.ColControlAtlas != null)
                {
                    var ctrlAtlas = await dbContext.ColControlAtlas.Find(FilterDefinition<ControlSincronizacion>.Empty).FirstOrDefaultAsync();
                    if (ctrlAtlas != null)
                    {
                        fechaAtlas = ctrlAtlas.UltimaModificacionEC;
                    }

                    // Extraer dataset completo de Atlas
                    dataAtlas = new BackupCompletoDTO
                    {
                        OrigenDatos = "MongoDB Atlas",
                        FechaRespaldoEC = fechaAtlas ?? horaEjecucionEC,
                        UltimaModificacionEC = fechaAtlas ?? horaEjecucionEC,
                        ControlSincronizacion = ctrlAtlas,
                        Usuarios = await dbContext.ColUsuariosAtlas.Find(FilterDefinition<Usuario>.Empty).ToListAsync(),
                        Roles = await dbContext.ColRolesAtlas.Find(FilterDefinition<Rol>.Empty).ToListAsync(),
                        Notificaciones = await dbContext.ColNotificacionesAtlas.Find(FilterDefinition<Notificacion>.Empty).ToListAsync(),
                        PlanCuentas = await dbContext.ColCuentasAtlas.Find(FilterDefinition<CuentaContable>.Empty).ToListAsync(),
                        AsientosContables = await dbContext.ColAsientosAtlas.Find(FilterDefinition<AsientoContable>.Empty).ToListAsync()
                    };

                    if (fechaAtlas == null && dataAtlas.TotalDocumentos > 0)
                    {
                        fechaAtlas = horaEjecucionEC.AddMinutes(-1);
                        dataAtlas.UltimaModificacionEC = fechaAtlas.Value;
                    }

                    atlasDisponible = true;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[SyncService] Atlas no disponible o timeout: {ex.Message}");
                resultado.AtlasEstado = "Fuera de Línea / No disponible";
            }

            // 1.2 LECTURA LOCALHOST
            try
            {
                var settingsLocal = MongoClientSettings.FromConnectionString(LocalConnectionString);
                settingsLocal.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
                settingsLocal.ConnectTimeout = TimeSpan.FromSeconds(2);

                var clientLocal = new MongoClient(settingsLocal);
                var dbLocal = clientLocal.GetDatabase(PrimaryDatabaseName);

                var colCtrlLocal = dbLocal.GetCollection<ControlSincronizacion>("controlSincronizacion");
                var ctrlLocal = await colCtrlLocal.Find(FilterDefinition<ControlSincronizacion>.Empty).FirstOrDefaultAsync();
                if (ctrlLocal != null)
                {
                    fechaLocal = ctrlLocal.UltimaModificacionEC;
                }

                dataLocal = new BackupCompletoDTO
                {
                    OrigenDatos = "MongoDB Localhost",
                    FechaRespaldoEC = fechaLocal ?? horaEjecucionEC,
                    UltimaModificacionEC = fechaLocal ?? horaEjecucionEC,
                    ControlSincronizacion = ctrlLocal,
                    Usuarios = await dbLocal.GetCollection<Usuario>("usuarios").Find(FilterDefinition<Usuario>.Empty).ToListAsync(),
                    Roles = await dbLocal.GetCollection<Rol>("roles").Find(FilterDefinition<Rol>.Empty).ToListAsync(),
                    Notificaciones = await dbLocal.GetCollection<Notificacion>("notificaciones").Find(FilterDefinition<Notificacion>.Empty).ToListAsync(),
                    PlanCuentas = await dbLocal.GetCollection<CuentaContable>("cuentasContables").Find(FilterDefinition<CuentaContable>.Empty).ToListAsync(),
                    AsientosContables = await dbLocal.GetCollection<AsientoContable>("asientosContables").Find(FilterDefinition<AsientoContable>.Empty).ToListAsync()
                };

                if (fechaLocal == null && dataLocal.TotalDocumentos > 0)
                {
                    fechaLocal = horaEjecucionEC.AddMinutes(-2);
                    dataLocal.UltimaModificacionEC = fechaLocal.Value;
                }

                localDisponible = true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[SyncService] Localhost no disponible: {ex.Message}");
                resultado.LocalEstado = "Fuera de Línea";
            }

            // 1.3 LECTURA ARCHIVO JSON DE RESPALDO
            try
            {
                var posiblesRutas = ObtenerRutasPosiblesJson();
                string rutaJsonValida = posiblesRutas.FirstOrDefault(File.Exists);

                if (!string.IsNullOrEmpty(rutaJsonValida) && File.Exists(rutaJsonValida))
                {
                    string rawJson = File.ReadAllText(rutaJsonValida);
                    dataJson = JsonConvert.DeserializeObject<BackupCompletoDTO>(rawJson);
                    if (dataJson != null)
                    {
                        fechaJson = dataJson.UltimaModificacionEC != default(DateTime) 
                            ? dataJson.UltimaModificacionEC 
                            : (dataJson.FechaRespaldoEC != default(DateTime) ? dataJson.FechaRespaldoEC : File.GetLastWriteTime(rutaJsonValida));

                        dataJson.UltimaModificacionEC = fechaJson.Value;
                        jsonDisponible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[SyncService] Error al leer archivo JSON: {ex.Message}");
                resultado.JsonEstado = "Error de Lectura";
            }

            // =========================================================================
            // PASO 2: Determinación de la fuente más reciente (FuenteValida)
            // =========================================================================
            BackupCompletoDTO fuenteValida = null;
            string nombreFuenteGanadora = "Ninguna";
            DateTime fechaGanadora = DateTime.MinValue;

            if (atlasDisponible && dataAtlas != null && dataAtlas.TotalDocumentos > 0 && fechaAtlas.HasValue && fechaAtlas.Value > fechaGanadora)
            {
                fuenteValida = dataAtlas;
                nombreFuenteGanadora = "MongoDB Atlas (Nube)";
                fechaGanadora = fechaAtlas.Value;
            }

            if (localDisponible && dataLocal != null && dataLocal.TotalDocumentos > 0 && fechaLocal.HasValue && fechaLocal.Value > fechaGanadora)
            {
                fuenteValida = dataLocal;
                nombreFuenteGanadora = "MongoDB Localhost (Local)";
                fechaGanadora = fechaLocal.Value;
            }

            if (jsonDisponible && dataJson != null && dataJson.TotalDocumentos > 0 && fechaJson.HasValue && fechaJson.Value > fechaGanadora)
            {
                fuenteValida = dataJson;
                nombreFuenteGanadora = "Archivo JSON de Respaldo (Servidor AWS / App_Data)";
                fechaGanadora = fechaJson.Value;
            }

            // Si ninguna fuente tenía fecha explícita pero hay datos disponibles, usar la primera que tenga registros
            if (fuenteValida == null)
            {
                if (atlasDisponible && dataAtlas != null && dataAtlas.TotalDocumentos > 0)
                {
                    fuenteValida = dataAtlas;
                    nombreFuenteGanadora = "MongoDB Atlas (Nube)";
                    fechaGanadora = horaEjecucionEC;
                }
                else if (localDisponible && dataLocal != null && dataLocal.TotalDocumentos > 0)
                {
                    fuenteValida = dataLocal;
                    nombreFuenteGanadora = "MongoDB Localhost (Local)";
                    fechaGanadora = horaEjecucionEC;
                }
                else if (jsonDisponible && dataJson != null && dataJson.TotalDocumentos > 0)
                {
                    fuenteValida = dataJson;
                    nombreFuenteGanadora = "Archivo JSON de Respaldo (Servidor AWS / App_Data)";
                    fechaGanadora = horaEjecucionEC;
                }
            }

            if (fuenteValida == null)
            {
                resultado.Success = false;
                resultado.Mensaje = "No se encontraron datos válidos en ninguna de las fuentes (Atlas, Localhost ni JSON).";
                return resultado;
            }

            resultado.FuenteUtilizada = nombreFuenteGanadora;
            resultado.FechaDatosRestauradosEC = fechaGanadora;
            resultado.TotalUsuarios = fuenteValida.Usuarios?.Count ?? 0;
            resultado.TotalRoles = fuenteValida.Roles?.Count ?? 0;
            resultado.TotalCuentas = fuenteValida.PlanCuentas?.Count ?? 0;
            resultado.TotalAsientos = fuenteValida.AsientosContables?.Count ?? 0;
            resultado.TotalNotificaciones = fuenteValida.Notificaciones?.Count ?? 0;

            // Asegurar que el objeto de control tenga la marca correcta
            var ctrlSincronizado = new ControlSincronizacion
            {
                Id = "66ca00000000000000000001",
                UltimaModificacionEC = fechaGanadora,
                UltimaModificacionUtc = DateTime.UtcNow,
                OrigenUltimoCambio = $"AutoHealingSync ({nombreFuenteGanadora})",
                DetalleAccion = "Restauración y Sincronización Automática",
                TotalDocumentos = fuenteValida.TotalDocumentos
            };
            fuenteValida.ControlSincronizacion = ctrlSincronizado;
            fuenteValida.UltimaModificacionEC = fechaGanadora;

            // =========================================================================
            // PASO 3 & 4: Propagación y Réplica (Sobreescribir fuentes desactualizadas)
            // =========================================================================

            // 3.1 RÉPLICA A ATLAS
            if (atlasDisponible)
            {
                try
                {
                    if (nombreFuenteGanadora != "MongoDB Atlas (Nube)")
                    {
                        await RestaurarColeccionesEnDb(dbContext.ColUsuariosAtlas.Database, fuenteValida);
                        resultado.AtlasEstado = "Restaurado y Sincronizado";
                    }
                    else
                    {
                        resultado.AtlasEstado = "Fuente Original (Al día)";
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[SyncService] Falló réplica a Atlas: {ex.Message}");
                    resultado.AtlasEstado = "Error al replicar (" + ex.Message + ")";
                }
            }
            else
            {
                resultado.AtlasEstado = "Atlas Fuera de Línea (Omitido con éxito)";
            }

            // 3.2 RÉPLICA A LOCALHOST (ContabilidadDB y ContabilidadDB_Backup)
            if (localDisponible)
            {
                try
                {
                    var settingsLocal = MongoClientSettings.FromConnectionString(LocalConnectionString);
                    settingsLocal.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
                    var clientLocal = new MongoClient(settingsLocal);

                    // Restaurar en ContabilidadDB
                    var dbLocalPrincipal = clientLocal.GetDatabase(PrimaryDatabaseName);
                    await RestaurarColeccionesEnDb(dbLocalPrincipal, fuenteValida);

                    // Restaurar en ContabilidadDB_Backup
                    var dbLocalBackup = clientLocal.GetDatabase(BackupDatabaseName);
                    await RestaurarColeccionesEnDb(dbLocalBackup, fuenteValida);

                    if (nombreFuenteGanadora != "MongoDB Localhost (Local)")
                    {
                        resultado.LocalEstado = "Restaurado y Sincronizado";
                    }
                    else
                    {
                        resultado.LocalEstado = "Fuente Original (Al día)";
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[SyncService] Falló réplica a Localhost: {ex.Message}");
                    resultado.LocalEstado = "Error al replicar (" + ex.Message + ")";
                }
            }
            else
            {
                resultado.LocalEstado = "Localhost Fuera de Línea";
            }

            // 3.3 RÉPLICA AL ARCHIVO JSON DEL SERVIDOR AWS (~/App_Data/Respaldos)
            try
            {
                string rutaJson = ObtenerRutaArchivoJson();
                string jsonActualizado = JsonConvert.SerializeObject(fuenteValida, Formatting.Indented);
                File.WriteAllText(rutaJson, jsonActualizado, System.Text.Encoding.UTF8);

                if (!nombreFuenteGanadora.Contains("Archivo JSON de Respaldo"))
                {
                    resultado.JsonEstado = "Actualizado en Servidor AWS (App_Data/Respaldos)";
                }
                else
                {
                    resultado.JsonEstado = "Fuente Original (Al día)";
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[SyncService] Error al escribir archivo JSON: {ex.Message}");
                resultado.JsonEstado = "Error al guardar (" + ex.Message + ")";
            }

            resultado.Success = true;

            if (!atlasDisponible)
            {
                resultado.Mensaje = "Sincronizado en Local (Atlas Fuera de Línea). Los datos locales y el archivo de respaldo están 100% actualizados.";
            }
            else
            {
                resultado.Mensaje = $"Sincronización integral completada exitosamente utilizando como base: {nombreFuenteGanadora}.";
            }

            return resultado;
        }

        /// <summary>
        /// Reemplaza e inserta masivamente los documentos de la fuente ganadora en una base de datos de destino.
        /// </summary>
        private static async Task RestaurarColeccionesEnDb(IMongoDatabase db, BackupCompletoDTO data)
        {
            if (db == null || data == null) return;

            // 1. Usuarios
            var colUsuarios = db.GetCollection<Usuario>("usuarios");
            await colUsuarios.DeleteManyAsync(FilterDefinition<Usuario>.Empty);
            if (data.Usuarios != null && data.Usuarios.Count > 0)
            {
                await colUsuarios.InsertManyAsync(data.Usuarios);
            }

            // 2. Roles
            var colRoles = db.GetCollection<Rol>("roles");
            await colRoles.DeleteManyAsync(FilterDefinition<Rol>.Empty);
            if (data.Roles != null && data.Roles.Count > 0)
            {
                await colRoles.InsertManyAsync(data.Roles);
            }

            // 3. Notificaciones
            var colNotif = db.GetCollection<Notificacion>("notificaciones");
            await colNotif.DeleteManyAsync(FilterDefinition<Notificacion>.Empty);
            if (data.Notificaciones != null && data.Notificaciones.Count > 0)
            {
                await colNotif.InsertManyAsync(data.Notificaciones);
            }

            // 4. Plan de Cuentas
            var colCuentas = db.GetCollection<CuentaContable>("cuentasContables");
            await colCuentas.DeleteManyAsync(FilterDefinition<CuentaContable>.Empty);
            if (data.PlanCuentas != null && data.PlanCuentas.Count > 0)
            {
                await colCuentas.InsertManyAsync(data.PlanCuentas);
            }

            // 5. Asientos Contables
            var colAsientos = db.GetCollection<AsientoContable>("asientosContables");
            await colAsientos.DeleteManyAsync(FilterDefinition<AsientoContable>.Empty);
            if (data.AsientosContables != null && data.AsientosContables.Count > 0)
            {
                await colAsientos.InsertManyAsync(data.AsientosContables);
            }

            // 6. ControlSincronizacion
            var colControl = db.GetCollection<ControlSincronizacion>("controlSincronizacion");
            if (data.ControlSincronizacion != null)
            {
                await colControl.ReplaceOneAsync(x => x.Id == data.ControlSincronizacion.Id, data.ControlSincronizacion, new ReplaceOptions { IsUpsert = true });
            }
        }

        /// <summary>
        /// Despacha la sincronización y escaneo automático en segundo plano para no bloquear el flujo web.
        /// </summary>
        public static void EjecutarSincronizacionEnSegundoPlano()
        {
            Task.Run(async () =>
            {
                try
                {
                    var sync = new SyncService();
                    await sync.SincronizarYRestaurarAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[SyncService - AutoSyncBackground]: {ex.Message}");
                }
            });
        }
    }
}
