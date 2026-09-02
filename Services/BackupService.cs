using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Hosting;
using MongoDB.Driver;
using Newtonsoft.Json;
using SistemaContable.Data;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio centralizado de Respaldo Integral para la nube (Render / AWS).
    /// Extrae los datos de MongoDB Atlas y genera el archivo JSON persistente en App_Data/Respaldos.
    /// </summary>
    public class BackupService
    {
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

        public static void DispararRespaldoEnSegundoPlano()
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    await EjecutarRespaldoFullAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[BackupService Aviso]: {ex.Message}");
                }
            });
        }

        public static void EjecutarRespaldoFullNoBloqueante() => DispararRespaldoEnSegundoPlano();

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

        public static string ObtenerRutaCarpetaRespaldos()
        {
            var candidatos = new System.Collections.Generic.List<string>();

            try
            {
                string mapPath = HostingEnvironment.MapPath("~/App_Data/Respaldos/");
                if (!string.IsNullOrEmpty(mapPath)) candidatos.Add(mapPath);
            }
            catch { }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir)) candidatos.Add(Path.Combine(baseDir, "App_Data", "Respaldos"));
            }
            catch { }

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

        public static string ObtenerRutaArchivoJson()
        {
            string folderPath = ObtenerRutaCarpetaRespaldos();
            return Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");
        }

        /// <summary>
        /// Proceso de Respaldo Integral:
        /// - Paso A: Extracción de todas las colecciones desde MongoDB Atlas.
        /// - Paso B: Serialización JSON en Servidor (~/App_Data/Respaldos/Respaldo_Contabilidad_Full.json).
        /// </summary>
        public static async Task EjecutarRespaldoFullAsync()
        {
            var context = MongoDbContext.Instance;
            if (!context.IsConnected)
            {
                Trace.TraceWarning("[BackupService]: Contexto de MongoDB Atlas no conectado. Se omite el respaldo.");
                return;
            }

            var fechaEC = ObtenerHoraEcuador();
            var backupDto = new BackupCompletoDTO
            {
                FechaRespaldoEC = fechaEC,
                OrigenDatos = "MongoDB Atlas (Nube)",
                BaseDeDatosOrigen = context.DatabaseName
            };

            // PASO A: Consultar colecciones actuales en MongoDB Atlas
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
                Trace.TraceError($"[BackupService - Error al leer colecciones de Atlas]: {ex.Message}");
                return;
            }

            // PASO B: Serializar a JSON y Guardar en Servidor
            try
            {
                string folderPath = ObtenerRutaCarpetaRespaldos();
                string fullPath = Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");

                string jsonContent = JsonConvert.SerializeObject(backupDto, Formatting.Indented);
                File.WriteAllText(fullPath, jsonContent, System.Text.Encoding.UTF8);

                Trace.TraceInformation($"[BackupService Exitoso]: Respaldo guardado en '{fullPath}'. Total documentos: {backupDto.TotalDocumentos}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[BackupService Advertencia al escribir JSON]: {ex.Message}");
            }
        }
    }
}
