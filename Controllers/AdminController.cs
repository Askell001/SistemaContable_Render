using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using SistemaContable.Filters;
using SistemaContable.Models;
using SistemaContable.Services;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador administrativo exclusivo para el Administrador para la ejecución de Auto-Healing Sync,
    /// restauración masiva de datos y diagnóstico del sistema.
    /// </summary>
    [SessionAuthorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly SyncService _syncService = new SyncService();

        // POST: /Admin/EjecutarSincronizacion
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> EjecutarSincronizacion()
        {
            try
            {
                var resultado = await _syncService.SincronizarYRestaurarAsync();

                return Json(new
                {
                    success = resultado.Success,
                    mensaje = resultado.Mensaje,
                    fuenteUtilizada = resultado.FuenteUtilizada,
                    fechaDatosRestauradosEC = resultado.FechaDatosRestauradosEC.HasValue 
                        ? resultado.FechaDatosRestauradosEC.Value.ToString("dd/MM/yyyy HH:mm:ss") + " (UTC-5)"
                        : "No disponible",
                    fechaEjecucionEC = resultado.FechaEjecucionEC.ToString("dd/MM/yyyy HH:mm:ss") + " (UTC-5)",
                    atlasEstado = resultado.AtlasEstado,
                    localEstado = resultado.LocalEstado,
                    jsonEstado = resultado.JsonEstado,
                    totalUsuarios = resultado.TotalUsuarios,
                    totalRoles = resultado.TotalRoles,
                    totalCuentas = resultado.TotalCuentas,
                    totalAsientos = resultado.TotalAsientos,
                    totalDocumentos = resultado.TotalDocumentos
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    mensaje = "Error al ejecutar el proceso de sincronización: " + ex.Message,
                    fuenteUtilizada = "Ninguna",
                    atlasEstado = "Error",
                    localEstado = "Error",
                    jsonEstado = "Error"
                });
            }
        }
    }
}
