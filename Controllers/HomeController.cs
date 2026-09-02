using System;
using System.Web.Mvc;
using SistemaContable.Data;
using MongoDB.Driver;

namespace SistemaContable.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            var dbContext = MongoDbContext.Instance;
            
            ViewBag.ActiveConnection = dbContext.ActiveConnectionName;
            ViewBag.DatabaseName = dbContext.DatabaseName;
            ViewBag.IsConnected = dbContext.IsConnected;
            ViewBag.LastErrorMessage = dbContext.LastErrorMessage;

            if (dbContext.IsConnected)
            {
                var pingResult = dbContext.TestConnection();
                ViewBag.PingSuccess = pingResult.Success;
                ViewBag.PingElapsedMs = pingResult.ElapsedMs;
                ViewBag.PingMessage = pingResult.Message;

                if (pingResult.Success)
                {
                    try
                    {
                        // Contadores de colecciones (con timeout corto)
                        ViewBag.TotalUsuarios = dbContext.Usuarios.EstimatedDocumentCount();
                        ViewBag.TotalRoles = dbContext.Roles.EstimatedDocumentCount();
                        ViewBag.TotalCuentas = dbContext.CuentasContables.EstimatedDocumentCount();
                        ViewBag.TotalAsientos = dbContext.AsientosContables.EstimatedDocumentCount();
                        ViewBag.TotalNotificaciones = dbContext.Notificaciones.EstimatedDocumentCount();
                    }
                    catch (Exception ex)
                    {
                        ViewBag.StatsError = ex.Message;
                    }
                }
            }
            else
            {
                ViewBag.PingSuccess = false;
                ViewBag.PingMessage = "No hay conexión activa inicializada con MongoDB.";
            }

            return View();
        }
    }
}
