using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MongoDB.Driver;
using Newtonsoft.Json;
using SistemaContable.Data;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio centralizado para la generación de respaldos integrales del sistema:
    /// 1. Exportación a JSON estructurado en la carpeta del Escritorio "Respaldo base de datos".
    /// 2. Sincronización masiva (InsertMany) hacia la base de datos "ContabilidadDB_Backup" en MongoDB Local (localhost:27017).
    /// </summary>
    public static class BackupService
    {
        private const string LocalConnectionString = "mongodb://localhost:27017";
        private const string BackupDatabaseName = "ContabilidadDB_Backup";

        /// <summary>
        /// Obtiene la hora actual en la zona horaria de Ecuador (SA Pacific Standard Time / UTC-5).
        /// </summary>
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
        /// Ejecuta el respaldo completo en segundo plano (No bloqueante) de forma segura y tolerante a fallos.
        /// </summary>
        public static void EjecutarRespaldoFullNoBloqueante()
        {
            Task.Run(async () =>
            {
                try
                {
                    await EjecutarRespaldoFullAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[BackupService Error No Bloqueante]: {ex.Message}");
                    Debug.WriteLine($"[BackupService Error]: {ex}");
                }
            });
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
            var candidatos = new System.Collections.Generic.List<string>();

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
        /// Obtiene la ruta completa al archivo Respaldo_Contabilidad_Full.json en el servidor.
        /// </summary>
        public static string ObtenerRutaArchivoJson()
        {
            string folderPath = ObtenerRutaCarpetaRespaldos();
            return Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");
        }

        /// <summary>
        /// Proceso de Respaldo Integral Completo:
        /// - Paso A: Extracción de todas las colecciones desde MongoDB Atlas.
        /// - Paso B: Serialización JSON en Servidor AWS (~/App_Data/Respaldos/Respaldo_Contabilidad_Full.json).
        /// - Paso C: Vaciado y carga masiva (InsertMany) en MongoDB Local (ContabilidadDB_Backup).
        /// </summary>
        public static async Task EjecutarRespaldoFullAsync()
        {
            var context = MongoDbContext.Instance;
            if (!context.IsConnected)
            {
                Trace.TraceWarning("[BackupService]: Contexto de MongoDB no inicializado. Se omite el respaldo.");
                return;
            }

            var fechaEC = ObtenerHoraEcuador();
            var backupDto = new BackupCompletoDTO
            {
                FechaRespaldoEC = fechaEC,
                OrigenDatos = context.ActiveConnectionName,
                BaseDeDatosOrigen = context.DatabaseName
            };

            // =========================================================================
            // PASO A: Consultar colecciones actuales en MongoDB Atlas / Primario
            // =========================================================================
            try
            {
                if (context.Usuarios != null)
                {
                    backupDto.Usuarios = await context.Usuarios.Find(Builders<Usuario>.Filter.Empty).ToListAsync();
                }
                if (context.Roles != null)
                {
                    backupDto.Roles = await context.Roles.Find(Builders<Rol>.Filter.Empty).ToListAsync();
                }
                if (context.Notificaciones != null)
                {
                    backupDto.Notificaciones = await context.Notificaciones.Find(Builders<Notificacion>.Filter.Empty).ToListAsync();
                }
                if (context.CuentasContables != null)
                {
                    backupDto.PlanCuentas = await context.CuentasContables.Find(Builders<CuentaContable>.Filter.Empty).ToListAsync();
                }
                if (context.AsientosContables != null)
                {
                    backupDto.AsientosContables = await context.AsientosContables.Find(Builders<AsientoContable>.Filter.Empty).ToListAsync();
                }
                if (context.ControlSincronizacion != null)
                {
                    backupDto.ControlSincronizacion = await context.ControlSincronizacion.Find(Builders<ControlSincronizacion>.Filter.Empty).FirstOrDefaultAsync();
                }

                if (backupDto.ControlSincronizacion != null)
                {
                    backupDto.UltimaModificacionEC = backupDto.ControlSincronizacion.UltimaModificacionEC;
                }
                else
                {
                    backupDto.UltimaModificacionEC = fechaEC;
                    backupDto.ControlSincronizacion = new ControlSincronizacion
                    {
                        UltimaModificacionEC = fechaEC,
                        UltimaModificacionUtc = DateTime.UtcNow,
                        DetalleAccion = "Respaldo Automático",
                        TotalDocumentos = backupDto.TotalDocumentos
                    };
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[BackupService - Paso A Fallido al leer colecciones]: {ex.Message}");
                // Si falla la lectura de Atlas, no continuamos para no sobrescribir con datos vacíos
                return;
            }

            // =========================================================================
            // PASO B: Serializar a JSON y Guardar en Servidor AWS (~/App_Data/Respaldos)
            // =========================================================================
            try
            {
                string folderPath = ObtenerRutaCarpetaRespaldos();
                string rutaArchivoJson = Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");
                string jsonContenido = JsonConvert.SerializeObject(backupDto, Formatting.Indented);

                File.WriteAllText(rutaArchivoJson, jsonContenido, System.Text.Encoding.UTF8);
                Trace.TraceInformation($"[BackupService - Paso B Exitoso]: Archivo guardado en Servidor AWS: {rutaArchivoJson} ({backupDto.TotalDocumentos} documentos).");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[BackupService - Paso B Advertencia al guardar archivo en App_Data/Respaldos]: {ex.Message}");
            }

            // =========================================================================
            // PASO C: Sincronización a MongoDB Localhost (ContabilidadDB_Backup)
            // =========================================================================
            try
            {
                var settings = MongoClientSettings.FromConnectionString(LocalConnectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
                settings.ConnectTimeout = TimeSpan.FromSeconds(2);

                var localClient = new MongoClient(settings);
                var backupDb = localClient.GetDatabase(BackupDatabaseName);

                // 1. Usuarios
                if (backupDto.Usuarios != null && backupDto.Usuarios.Count > 0)
                {
                    var colUsuarios = backupDb.GetCollection<Usuario>("usuarios");
                    await colUsuarios.DeleteManyAsync(Builders<Usuario>.Filter.Empty);
                    await colUsuarios.InsertManyAsync(backupDto.Usuarios);
                }

                // 2. Roles
                if (backupDto.Roles != null && backupDto.Roles.Count > 0)
                {
                    var colRoles = backupDb.GetCollection<Rol>("roles");
                    await colRoles.DeleteManyAsync(Builders<Rol>.Filter.Empty);
                    await colRoles.InsertManyAsync(backupDto.Roles);
                }

                // 3. Notificaciones
                if (backupDto.Notificaciones != null && backupDto.Notificaciones.Count > 0)
                {
                    var colNotificaciones = backupDb.GetCollection<Notificacion>("notificaciones");
                    await colNotificaciones.DeleteManyAsync(Builders<Notificacion>.Filter.Empty);
                    await colNotificaciones.InsertManyAsync(backupDto.Notificaciones);
                }

                // 4. Plan de Cuentas
                if (backupDto.PlanCuentas != null && backupDto.PlanCuentas.Count > 0)
                {
                    var colCuentas = backupDb.GetCollection<CuentaContable>("cuentasContables");
                    await colCuentas.DeleteManyAsync(Builders<CuentaContable>.Filter.Empty);
                    await colCuentas.InsertManyAsync(backupDto.PlanCuentas);
                }

                // 5. Asientos Contables
                if (backupDto.AsientosContables != null && backupDto.AsientosContables.Count > 0)
                {
                    var colAsientos = backupDb.GetCollection<AsientoContable>("asientosContables");
                    await colAsientos.DeleteManyAsync(Builders<AsientoContable>.Filter.Empty);
                    await colAsientos.InsertManyAsync(backupDto.AsientosContables);
                }

                Trace.TraceInformation($"[BackupService - Paso C Exitoso]: Sincronización a '{BackupDatabaseName}' completada exitosamente.");
            }
            catch (Exception ex)
            {
                // Si MongoDB local está apagado o no responde, registramos advertencia sin interrumpir
                Trace.TraceWarning($"[BackupService - Paso C Advertencia]: No se pudo conectar a MongoDB Local ({LocalConnectionString}): {ex.Message}");
            }
        }
    }
}
