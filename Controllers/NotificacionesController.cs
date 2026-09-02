using System;
using System.Linq;
using System.Web.Mvc;
using SistemaContable.Filters;
using SistemaContable.Services;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador de endpoints AJAX para el sistema de notificaciones en tiempo real.
    /// </summary>
    [SessionAuthorize]
    public class NotificacionesController : Controller
    {
        private readonly NotificacionService _notificacionService;

        public NotificacionesController()
        {
            _notificacionService = new NotificacionService();
        }

        // GET: /Notificaciones/ObtenerNoLeidas
        [HttpGet]
        public JsonResult ObtenerNoLeidas()
        {
            try
            {
                string usuarioId = Session["UsuarioId"]?.ToString();
                if (string.IsNullOrEmpty(usuarioId))
                {
                    return Json(new { success = false, message = "Sesión no válida.", totalNoLeidas = 0, notificaciones = new object[0] }, JsonRequestBehavior.AllowGet);
                }

                var noLeidas = _notificacionService.ObtenerNoLeidasPorUsuario(usuarioId, limite: 8);
                long totalNoLeidas = _notificacionService.ContarNoLeidasPorUsuario(usuarioId);

                var items = noLeidas.Select(n => new
                {
                    id = n.Id,
                    mensaje = n.Mensaje,
                    tipo = n.Tipo,
                    fecha = n.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    hace = ObtenerTiempoRelativo(n.Fecha),
                    iconoCss = ObtenerIconoCss(n.Tipo),
                    badgeCss = ObtenerBadgeCss(n.Tipo)
                }).ToList();

                return Json(new
                {
                    success = true,
                    totalNoLeidas = totalNoLeidas,
                    notificaciones = items
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al obtener notificaciones: " + ex.Message,
                    totalNoLeidas = 0,
                    notificaciones = new object[0]
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: /Notificaciones/MarcarComoLeida
        [HttpPost]
        public JsonResult MarcarComoLeida(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "ID no proporcionado." });
            }

            bool resultado = _notificacionService.MarcarComoLeida(id);
            return Json(new { success = resultado });
        }

        // POST: /Notificaciones/MarcarTodasComoLeidas
        [HttpPost]
        public JsonResult MarcarTodasComoLeidas()
        {
            string usuarioId = Session["UsuarioId"]?.ToString();
            if (string.IsNullOrEmpty(usuarioId))
            {
                return Json(new { success = false, message = "Sesión expirada." });
            }

            bool resultado = _notificacionService.MarcarTodasComoLeidas(usuarioId);
            return Json(new { success = resultado });
        }

        // POST: /Notificaciones/CrearDemo (Helper para emitir una notificación rápida de prueba)
        [HttpPost]
        public JsonResult CrearDemo(string mensaje, string tipo)
        {
            string usuarioId = Session["UsuarioId"]?.ToString();
            if (string.IsNullOrEmpty(usuarioId))
            {
                return Json(new { success = false, message = "Sesión no válida." });
            }

            mensaje = string.IsNullOrWhiteSpace(mensaje) ? "Notificación de prueba generada en el sistema contable." : mensaje;
            tipo = string.IsNullOrWhiteSpace(tipo) ? "Info" : tipo;

            bool creada = _notificacionService.CrearNotificacion(usuarioId, mensaje, tipo);
            return Json(new { success = creada, message = creada ? "Notificación enviada correctamente." : "No se pudo crear." });
        }

        #region Helpers Visuales
        private static string ObtenerIconoCss(string tipo)
        {
            switch (tipo)
            {
                case "Alerta": return "bi-exclamation-triangle-fill text-warning";
                case "Exito": return "bi-check-circle-fill text-success";
                default: return "bi-info-circle-fill text-primary";
            }
        }

        private static string ObtenerBadgeCss(string tipo)
        {
            switch (tipo)
            {
                case "Alerta": return "bg-warning text-dark";
                case "Exito": return "bg-success text-white";
                default: return "bg-primary text-white";
            }
        }

        private static string ObtenerTiempoRelativo(DateTime fechaUtc)
        {
            var span = DateTime.UtcNow - fechaUtc;
            if (span.TotalMinutes < 1) return "Hace un momento";
            if (span.TotalMinutes < 60) return $"Hace {(int)span.TotalMinutes} min";
            if (span.TotalHours < 24) return $"Hace {(int)span.TotalHours} h";
            if (span.TotalDays < 7) return $"Hace {(int)span.TotalDays} d";
            return fechaUtc.ToLocalTime().ToString("dd/MM/yyyy");
        }
        #endregion
    }
}
