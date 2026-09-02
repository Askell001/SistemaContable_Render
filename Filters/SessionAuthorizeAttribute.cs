using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SistemaContable.Filters
{
    /// <summary>
    /// Filtro de autorización que verifica la existencia de sesión activa y opcionalmente roles específicos.
    /// Si el usuario no está autenticado, lo redirige de forma segura a /Account/Login preservando el ReturnUrl.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class SessionAuthorizeAttribute : ActionFilterAttribute
    {
        public string Roles { get; set; }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var session = filterContext.HttpContext.Session;
            var user = filterContext.HttpContext.User;

            // 1. Validar si existe sesión activa de usuario
            if (session == null || session["UsuarioId"] == null)
            {
                string returnUrl = filterContext.HttpContext.Request.RawUrl;

                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary
                    {
                        { "controller", "Account" },
                        { "action", "Login" },
                        { "returnUrl", returnUrl }
                    });
                return;
            }

            // 2. Validar roles si fueron especificados en el atributo (ej: Roles = "Admin,Contador")
            if (!string.IsNullOrEmpty(Roles))
            {
                string usuarioRol = session["Rol"]?.ToString() ?? string.Empty;
                var rolesPermitidos = Roles.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                
                bool tienePermiso = false;
                foreach (var rol in rolesPermitidos)
                {
                    if (string.Equals(rol.Trim(), usuarioRol.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        tienePermiso = true;
                        break;
                    }
                }

                if (!tienePermiso)
                {
                    // Si no tiene permisos, redirigir a una página de acceso no autorizado o al dashboard con alerta
                    filterContext.Controller.TempData["MensajeError"] = "No tienes permisos suficientes para acceder a este módulo.";
                    filterContext.Result = new RedirectToRouteResult(
                        new RouteValueDictionary
                        {
                            { "controller", "Dashboard" },
                            { "action", "Index" }
                        });
                    return;
                }
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
