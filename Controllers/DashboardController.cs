using System;
using System.Linq;
using System.Web.Mvc;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Filters;
using SistemaContable.Models;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador principal del panel de administración y métricas de la Intranet.
    /// </summary>
    [SessionAuthorize]
    public class DashboardController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;

        // GET: /Dashboard/Index
        public ActionResult Index()
        {
            string userRol = Session["Rol"]?.ToString() ?? "Lectura";
            string userEmpresa = Session["Empresa"]?.ToString() ?? "Empresa Principal S.A.";
            bool esLector = string.Equals(userRol, "Lectura", StringComparison.OrdinalIgnoreCase) || string.Equals(userRol, "Lector", StringComparison.OrdinalIgnoreCase);

            ViewBag.NombreUsuario = Session["Nombre"] ?? "Usuario";
            ViewBag.RolUsuario = userRol;
            ViewBag.EmpresaUsuario = userEmpresa;
            ViewBag.ActiveConnection = _context.ActiveConnectionName;
            ViewBag.DatabaseName = _context.DatabaseName;

            try
            {
                if (_context.IsConnected)
                {
                    ViewBag.TotalUsuarios = _context.Usuarios?.CountDocuments(FilterDefinition<Usuario>.Empty) ?? 0;
                    ViewBag.TotalCuentas = _context.CuentasContables?.CountDocuments(FilterDefinition<CuentaContable>.Empty) ?? 0;

                    var filtroAsientos = FilterDefinition<AsientoContable>.Empty;
                    if (esLector)
                    {
                        filtroAsientos = Builders<AsientoContable>.Filter.Or(
                            Builders<AsientoContable>.Filter.Eq(a => a.Empresa, userEmpresa),
                            Builders<AsientoContable>.Filter.Eq(a => a.Empresa, null),
                            Builders<AsientoContable>.Filter.Eq(a => a.Empresa, "")
                        );
                    }

                    ViewBag.TotalAsientos = _context.AsientosContables?.CountDocuments(filtroAsientos) ?? 0;
                    ViewBag.TotalNotificaciones = _context.Notificaciones?.CountDocuments(FilterDefinition<Notificacion>.Empty) ?? 0;

                    // Obtener últimos asientos contables recientes
                    if (_context.AsientosContables != null)
                    {
                        var ultimosAsientos = _context.AsientosContables
                            .Find(filtroAsientos)
                            .SortByDescending(a => a.Fecha)
                            .Limit(5)
                            .ToList();
                        ViewBag.UltimosAsientos = ultimosAsientos;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMetricas = ex.Message;
            }

            return View();
        }
    }
}
