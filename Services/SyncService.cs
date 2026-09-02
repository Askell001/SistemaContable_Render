using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;
using MongoDB.Driver;
using Newtonsoft.Json;
using SistemaContable.Data;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio de Sincronización y Restauración Inteligente (Auto-Healing Sync) para MongoDB Atlas.
    /// Compara las marcas de tiempo entre MongoDB Atlas (Nube) y el archivo JSON en el servidor.
    /// </summary>
    public class SyncService
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
            var candidatos = new List<string>();

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

        public static List<string> ObtenerRutasPosiblesJson()
        {
            var rutas = new List<string>();

            try
            {
                string mapPath = HostingEnvironment.MapPath("~/App_Data/Respaldos/Respaldo_Contabilidad_Full.json");
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

        public static string ObtenerRutaArchivoJson()
        {
            string folderPath = ObtenerRutaCarpetaRespaldos();
            return Path.Combine(folderPath, "Respaldo_Contabilidad_Full.json");
        }

        /// <summary>
        /// Sincronización y restauración con MongoDB Atlas.
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
            DateTime? fechaJson = null;

            BackupCompletoDTO dataAtlas = null;
            BackupCompletoDTO dataJson = null;

            bool atlasDisponible = false;
            bool jsonDisponible = false;

            // 1.1 LECTURA ATLAS
            try
            {
                if (dbContext.IsConnected && dbContext.ControlSincronizacion != null)
                {
                    var ctrlAtlas = await dbContext.ControlSincronizacion.Find(FilterDefinition<ControlSincronizacion>.Empty).FirstOrDefaultAsync();
                    if (ctrlAtlas != null)
                    {
                        fechaAtlas = ctrlAtlas.UltimaModificacionEC;
                    }

                    dataAtlas = new BackupCompletoDTO
                    {
                        OrigenDatos = "MongoDB Atlas",
                        FechaRespaldoEC = fechaAtlas ?? horaEjecucionEC,
                        UltimaModificacionEC = fechaAtlas ?? horaEjecucionEC,
                        ControlSincronizacion = ctrlAtlas,
                        Usuarios = await dbContext.Usuarios.Find(FilterDefinition<Usuario>.Empty).ToListAsync(),
                        Roles = await dbContext.Roles.Find(FilterDefinition<Rol>.Empty).ToListAsync(),
                        Notificaciones = await dbContext.Notificaciones.Find(FilterDefinition<Notificacion>.Empty).ToListAsync(),
                        PlanCuentas = await dbContext.CuentasContables.Find(FilterDefinition<CuentaContable>.Empty).ToListAsync(),
                        AsientosContables = await dbContext.AsientosContables.Find(FilterDefinition<AsientoContable>.Empty).ToListAsync()
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
                Trace.TraceWarning($"[SyncService] Atlas no disponible: {ex.Message}");
                resultado.AtlasEstado = "Fuera de Línea";
            }

            // 1.2 LECTURA ARCHIVO JSON
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

            // PASO 2: Selección de la fuente ganadora
            BackupCompletoDTO fuenteValida = null;
            string nombreFuenteGanadora = "Ninguna";
            DateTime fechaGanadora = DateTime.MinValue;

            if (atlasDisponible && dataAtlas != null && dataAtlas.TotalDocumentos > 0 && fechaAtlas.HasValue && fechaAtlas.Value > fechaGanadora)
            {
                fuenteValida = dataAtlas;
                nombreFuenteGanadora = "MongoDB Atlas (Nube)";
                fechaGanadora = fechaAtlas.Value;
            }

            if (jsonDisponible && dataJson != null && dataJson.TotalDocumentos > 0 && fechaJson.HasValue && fechaJson.Value > fechaGanadora)
            {
                fuenteValida = dataJson;
                nombreFuenteGanadora = "Archivo JSON de Respaldo (Servidor)";
                fechaGanadora = fechaJson.Value;
            }

            if (fuenteValida == null)
            {
                if (atlasDisponible && dataAtlas != null && dataAtlas.TotalDocumentos > 0)
                {
                    fuenteValida = dataAtlas;
                    nombreFuenteGanadora = "MongoDB Atlas (Nube)";
                    fechaGanadora = horaEjecucionEC;
                }
                else if (jsonDisponible && dataJson != null && dataJson.TotalDocumentos > 0)
                {
                    fuenteValida = dataJson;
                    nombreFuenteGanadora = "Archivo JSON de Respaldo (Servidor)";
                    fechaGanadora = horaEjecucionEC;
                }
            }

            if (fuenteValida == null)
            {
                resultado.Success = false;
                resultado.Mensaje = "No se encontraron datos en MongoDB Atlas ni en el archivo de respaldo.";
                return resultado;
            }

            resultado.FuenteUtilizada = nombreFuenteGanadora;
            resultado.FechaDatosRestauradosEC = fechaGanadora;
            resultado.TotalUsuarios = fuenteValida.Usuarios?.Count ?? 0;
            resultado.TotalRoles = fuenteValida.Roles?.Count ?? 0;
            resultado.TotalCuentas = fuenteValida.PlanCuentas?.Count ?? 0;
            resultado.TotalAsientos = fuenteValida.AsientosContables?.Count ?? 0;
            resultado.TotalNotificaciones = fuenteValida.Notificaciones?.Count ?? 0;

            var ctrlSincronizado = new ControlSincronizacion
            {
                Id = "66ca00000000000000000001",
                UltimaModificacionEC = fechaGanadora,
                UltimaModificacionUtc = DateTime.UtcNow,
                OrigenUltimoCambio = nombreFuenteGanadora,
                DetalleAccion = $"Auto-Healing Sync ejecutado ({fuenteValida.TotalDocumentos} documentos)",
                TotalDocumentos = fuenteValida.TotalDocumentos
            };

            // PASO 3: Restaurar en Atlas si la fuente ganadora fue el JSON
            if (atlasDisponible && nombreFuenteGanadora != "MongoDB Atlas (Nube)")
            {
                try
                {
                    if (fuenteValida.Usuarios?.Count > 0)
                    {
                        await dbContext.Usuarios.DeleteManyAsync(FilterDefinition<Usuario>.Empty);
                        await dbContext.Usuarios.InsertManyAsync(fuenteValida.Usuarios);
                    }
                    if (fuenteValida.Roles?.Count > 0)
                    {
                        await dbContext.Roles.DeleteManyAsync(FilterDefinition<Rol>.Empty);
                        await dbContext.Roles.InsertManyAsync(fuenteValida.Roles);
                    }
                    if (fuenteValida.PlanCuentas?.Count > 0)
                    {
                        await dbContext.CuentasContables.DeleteManyAsync(FilterDefinition<CuentaContable>.Empty);
                        await dbContext.CuentasContables.InsertManyAsync(fuenteValida.PlanCuentas);
                    }
                    if (fuenteValida.AsientosContables?.Count > 0)
                    {
                        await dbContext.AsientosContables.DeleteManyAsync(FilterDefinition<AsientoContable>.Empty);
                        await dbContext.AsientosContables.InsertManyAsync(fuenteValida.AsientosContables);
                    }

                    await dbContext.ControlSincronizacion.ReplaceOneAsync(x => x.Id == ctrlSincronizado.Id, ctrlSincronizado, new ReplaceOptions { IsUpsert = true });
                    resultado.AtlasEstado = "Restaurado y Sincronizado";
                }
                catch (Exception ex)
                {
                    resultado.AtlasEstado = "Error al Restaurar (" + ex.Message + ")";
                }
            }
            else if (atlasDisponible)
            {
                resultado.AtlasEstado = "Fuente Original (Al día)";
            }

            // PASO 4: Actualizar archivo JSON en servidor
            try
            {
                string rutaJson = ObtenerRutaArchivoJson();
                string jsonActualizado = JsonConvert.SerializeObject(fuenteValida, Formatting.Indented);
                File.WriteAllText(rutaJson, jsonActualizado, System.Text.Encoding.UTF8);
                resultado.JsonEstado = "Actualizado en Servidor";
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[SyncService] Error al escribir JSON: {ex.Message}");
                resultado.JsonEstado = "Advertencia (" + ex.Message + ")";
            }

            resultado.LocalEstado = "MongoDB Atlas Centralizado";
            resultado.Success = true;
            resultado.Mensaje = $"Sincronización completada exitosamente utilizando como base: {nombreFuenteGanadora}.";
            return resultado;
        }
    }
}
