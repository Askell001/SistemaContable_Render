using System;
using System.Diagnostics;
using System.Web.Mvc;
using System.Web.Routing;
using SistemaContable.Data;

namespace SistemaContable
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            // Pre-cargar ensamblados de Razor en el AppDomain para Mono en Linux
            try
            {
                var razorSectionType = typeof(System.Web.WebPages.Razor.Configuration.RazorWebSectionGroup);
                Trace.WriteLine($"[Global.asax] Ensamblado Razor pre-cargado: {razorSectionType.Assembly.FullName}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[Global.asax] Aviso pre-cargando Razor: {ex.Message}");
            }

            try
            {
                AreaRegistration.RegisterAllAreas();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[Global.asax] Aviso en AreaRegistration: {ex.Message}");
            }
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Registrar Binder universal de decimales para compatibilidad total con comas y puntos
            ModelBinders.Binders.Add(typeof(decimal), new SistemaContable.Filters.InvariantDecimalModelBinder());
            ModelBinders.Binders.Add(typeof(decimal?), new SistemaContable.Filters.InvariantDecimalModelBinder());

            // Asegurar existencia de la carpeta App_Data/Respaldos al arrancar en el servidor
            try
            {
                string path = Services.BackupService.ObtenerRutaCarpetaRespaldos();
                Trace.WriteLine($"[Application_Start] Carpeta de respaldos inicializada en: {path}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[Application_Start] Advertencia al crear carpeta de respaldos: {ex.Message}");
            }

            // Diagnóstico inicial de conexión MongoDB al arrancar la aplicación
            try
            {
                var dbContext = MongoDbContext.Instance;
                if (dbContext.IsConnected)
                {
                    Trace.WriteLine($"[Application_Start] MongoDbContext inicializado con éxito ({dbContext.ActiveConnectionName}).");
                }
                else
                {
                    Trace.TraceWarning($"[Application_Start] Advertencia: MongoDbContext no pudo conectar: {dbContext.LastErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[Application_Start] Excepción no controlada en DbContext: {ex.Message}");
            }
        }

        protected void Application_BeginRequest()
        {
            Response.ContentEncoding = System.Text.Encoding.UTF8;
            Response.Charset = "utf-8";
        }
    }
}
