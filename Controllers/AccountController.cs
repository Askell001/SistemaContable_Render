using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using BCrypt.Net;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Models;
using SistemaContable.Services;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador responsable de la autenticación, inicio y cierre de sesión de usuarios.
    /// Ejecuta escaneo y sincronización automática de bases de datos en cada inicio de sesión.
    /// </summary>
    public class AccountController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Login(string returnUrl, string msg)
        {
            if (msg == "logout")
            {
                ViewBag.MensajeInfo = "Has cerrado sesión exitosamente.";
            }

            // Si ya está autenticado y no es un logout explícito, redirigir
            if (string.IsNullOrEmpty(msg) && User.Identity.IsAuthenticated && Session["UsuarioId"] != null)
            {
                return RedirectToLocal(returnUrl);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginViewModel());
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;

            // 1. Validar modelo de entrada
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // 2. Verificar que el contexto de MongoDB esté disponible
                if (!_context.IsConnected || _context.Usuarios == null)
                {
                    ModelState.AddModelError("", "No se puede establecer conexión con la base de datos MongoDB. Verifique su Web.config.");
                    return View(model);
                }

                string emailNormalizado = model.Correo.Trim().ToLowerInvariant();

                // 3. Buscar el usuario en MongoDB por correo electrónico
                var usuario = _context.Usuarios
                    .Find(u => u.Correo.ToLower() == emailNormalizado)
                    .FirstOrDefault();

                // Si no se encuentra de inmediato, ejecutar un escaneo rápido por si fue creado/restaurado en otra fuente
                if (usuario == null)
                {
                    try
                    {
                        var syncQuick = new SyncService();
                        var resSync = await syncQuick.SincronizarYRestaurarAsync();
                        usuario = _context.Usuarios
                            .Find(u => u.Correo.ToLower() == emailNormalizado)
                            .FirstOrDefault();
                    }
                    catch { }
                }

                if (usuario == null)
                {
                    // Mensaje genérico de seguridad
                    ModelState.AddModelError("", "Correo electrónico o contraseña incorrectos.");
                    return View(model);
                }

                // 4. Validar estado del usuario
                if (!usuario.Estado)
                {
                    ModelState.AddModelError("", "Su cuenta de usuario se encuentra desactivada. Contacte al administrador.");
                    return View(model);
                }

                // 5. Verificar contraseña mediante BCrypt
                bool passwordValida = false;
                try
                {
                    passwordValida = BCrypt.Net.BCrypt.Verify(model.Password, usuario.PasswordHash);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"[AccountController] Error al verificar hash BCrypt: {ex.Message}");
                    passwordValida = false;
                }

                if (!passwordValida)
                {
                    ModelState.AddModelError("", "Correo electrónico o contraseña incorrectos.");
                    return View(model);
                }

                // 6. Obtener información del Rol asociado
                string nombreRol = "Lectura";
                if (!string.IsNullOrEmpty(usuario.RolId) && _context.Roles != null)
                {
                    try
                    {
                        var rol = _context.Roles.Find(r => r.Id == usuario.RolId).FirstOrDefault();
                        if (rol != null && !string.IsNullOrEmpty(rol.NombreRol))
                        {
                            nombreRol = rol.NombreRol;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"[AccountController] No se pudo obtener el rol ({usuario.RolId}): {ex.Message}");
                    }
                }

                // 7. Establecer Cookie de Autenticación Forms
                FormsAuthentication.SetAuthCookie(usuario.Correo, model.RememberMe);

                // 8. Inicializar variables de sesión seguras
                Session["UsuarioId"] = usuario.Id;
                Session["Nombre"] = usuario.Nombre;
                Session["Correo"] = usuario.Correo;
                Session["Rol"] = nombreRol;
                Session["RolId"] = usuario.RolId;
                Session["Empresa"] = !string.IsNullOrEmpty(usuario.Empresa) ? usuario.Empresa : "Empresa Principal S.A.";
                Session["Autenticado"] = true;

                Trace.WriteLine($"[AccountController] Usuario autenticado exitosamente: {usuario.Correo} (Rol: {nombreRol}, Empresa: {Session["Empresa"]})");

                // 9. Auto-Healing Sync: Escaneo y sincronización automática de bases de datos al iniciar sesión
                try
                {
                    var syncService = new SyncService();
                    var res = await syncService.SincronizarYRestaurarAsync();
                    if (res != null && res.Success)
                    {
                        TempData["MensajeExito"] = $"¡Bienvenido {usuario.Nombre}! Bases de datos sincronizadas automáticamente desde {res.FuenteUtilizada}.";
                        Session["UltimaSincronizacion"] = res;
                        Trace.WriteLine($"[AccountController] Escaneo y sincronización al login completada: {res.TotalDocumentos} documentos al día.");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[AccountController] Aviso en escaneo de bases de datos al login: {ex.Message}");
                }

                return RedirectToLocal(returnUrl);
            }
            catch (Exception ex)
            {
                // Manejo de excepción controlado para evitar que la app colapse
                string errorMsg = $"[AccountController] Excepción durante el proceso de login: {ex.Message}";
                Trace.TraceError(errorMsg);
                Debug.WriteLine(errorMsg);

                ModelState.AddModelError("", "Ocurrió un error inesperado al procesar la solicitud. Intente nuevamente.");
                return View(model);
            }
        }

        // GET / POST: /Account/Logout
        [AllowAnonymous]
        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Post)]
        public ActionResult Logout()
        {
            try
            {
                FormsAuthentication.SignOut();
                Session.Clear();
                Session.Abandon();

                // Limpiar cookie de autenticación Forms
                if (Request.Cookies[FormsAuthentication.FormsCookieName] != null)
                {
                    var authCookie = new HttpCookie(FormsAuthentication.FormsCookieName, "")
                    {
                        Expires = DateTime.UtcNow.AddYears(-1),
                        Value = ""
                    };
                    Response.Cookies.Add(authCookie);
                }

                // Limpiar cookie de sesión ASP.NET
                if (Request.Cookies["ASP.NET_SessionId"] != null)
                {
                    var sessionCookie = new HttpCookie("ASP.NET_SessionId", "")
                    {
                        Expires = DateTime.UtcNow.AddYears(-1),
                        Value = ""
                    };
                    Response.Cookies.Add(sessionCookie);
                }

                Trace.WriteLine("[AccountController] Sesión cerrada exitosamente.");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[AccountController] Error durante Logout: {ex.Message}");
            }

            return RedirectToAction("Login", "Account", new { msg = "logout" });
        }

        // GET: /Account/SeedData (Crea un rol Admin y un usuario inicial para pruebas inmediatas si la BD está vacía)
        [HttpGet]
        [AllowAnonymous]
        public ActionResult SeedData()
        {
            try
            {
                if (!_context.IsConnected)
                {
                    ViewBag.SeedResult = "No hay conexión con MongoDB para inicializar datos.";
                    return View();
                }

                // Verificar si ya existen roles
                var totalRoles = _context.Roles.CountDocuments(FilterDefinition<Rol>.Empty);
                string adminRolId = null;

                if (totalRoles == 0)
                {
                    var rolAdmin = new Rol
                    {
                        NombreRol = "Admin",
                        Permisos = new List<string> { "Usuarios.Ver", "Usuarios.Crear", "Contabilidad.Todos", "Reportes.Todos" }
                    };
                    var rolContador = new Rol
                    {
                        NombreRol = "Contador",
                        Permisos = new List<string> { "Contabilidad.Todos", "Reportes.Ver" }
                    };
                    var rolLectura = new Rol
                    {
                        NombreRol = "Lectura",
                        Permisos = new List<string> { "Reportes.Ver" }
                    };

                    _context.InsertRolSimultaneo(rolAdmin);
                    _context.InsertRolSimultaneo(rolContador);
                    _context.InsertRolSimultaneo(rolLectura);
                    adminRolId = rolAdmin.Id;
                }
                else
                {
                    var rolAdmin = _context.Roles.Find(r => r.NombreRol == "Admin").FirstOrDefault();
                    if (rolAdmin != null) adminRolId = rolAdmin.Id;
                }

                // Verificar y crear usuarios base si no existen
                string contadorRolId = _context.Roles.Find(r => r.NombreRol == "Contador").FirstOrDefault()?.Id;
                string lecturaRolId = _context.Roles.Find(r => r.NombreRol == "Lectura").FirstOrDefault()?.Id;

                // 1. Admin
                var existeAdmin = _context.Usuarios.Find(u => u.Correo == "admin@contable.com").FirstOrDefault();
                if (existeAdmin == null && !string.IsNullOrEmpty(adminRolId))
                {
                    var adminUser = new Usuario
                    {
                        Nombre = "Administrador Principal",
                        Correo = "admin@contable.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123*"),
                        RolId = adminRolId,
                        Empresa = "Empresa Principal S.A.",
                        Estado = true,
                        FechaCreacion = DateTime.UtcNow
                    };
                    _context.InsertUsuarioSimultaneo(adminUser);
                }

                // 2. Contador
                var existeContador = _context.Usuarios.Find(u => u.Correo == "contador@contable.com").FirstOrDefault();
                if (existeContador == null && !string.IsNullOrEmpty(contadorRolId))
                {
                    var contadorUser = new Usuario
                    {
                        Nombre = "Lic. Roberto Contador",
                        Correo = "contador@contable.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Contador123*"),
                        RolId = contadorRolId,
                        Empresa = "Empresa Principal S.A.",
                        Estado = true,
                        FechaCreacion = DateTime.UtcNow
                    };
                    _context.InsertUsuarioSimultaneo(contadorUser);
                }

                // 3. Lector Empresa Principal
                var existeLector = _context.Usuarios.Find(u => u.Correo == "lector@contable.com").FirstOrDefault();
                if (existeLector == null && !string.IsNullOrEmpty(lecturaRolId))
                {
                    var lectorUser = new Usuario
                    {
                        Nombre = "Auditor Principal (Lector)",
                        Correo = "lector@contable.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Lector123*"),
                        RolId = lecturaRolId,
                        Empresa = "Empresa Principal S.A.",
                        Estado = true,
                        FechaCreacion = DateTime.UtcNow
                    };
                    _context.InsertUsuarioSimultaneo(lectorUser);
                }

                // 4. Lector Corporación Andina (Segunda Empresa de prueba)
                var existeLectorAndina = _context.Usuarios.Find(u => u.Correo == "lector.andina@contable.com").FirstOrDefault();
                if (existeLectorAndina == null && !string.IsNullOrEmpty(lecturaRolId))
                {
                    var lectorAndina = new Usuario
                    {
                        Nombre = "Gerente Corporación Andina (Lector)",
                        Correo = "lector.andina@contable.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Lector123*"),
                        RolId = lecturaRolId,
                        Empresa = "Corporación Andina S.A.",
                        Estado = true,
                        FechaCreacion = DateTime.UtcNow
                    };
                    _context.InsertUsuarioSimultaneo(lectorAndina);
                }

                ViewBag.SeedResult = "Datos y usuarios semilla listos (Admin, Contador, Lector).";
                // Verificar e inicializar catálogo de cuentas contables
                if (_context.CuentasContables != null)
                {
                    var totalCuentas = _context.CuentasContables.CountDocuments(FilterDefinition<CuentaContable>.Empty);
                    if (totalCuentas == 0)
                    {
                        var catalogoBase = new List<CuentaContable>
                        {
                            new CuentaContable { Codigo = "1", Nombre = "ACTIVO", Tipo = "Activo", Nivel = 1 },
                            new CuentaContable { Codigo = "1.1", Nombre = "Activo Corriente", Tipo = "Activo", Nivel = 2 },
                            new CuentaContable { Codigo = "1.1.01", Nombre = "Caja General", Tipo = "Activo", Nivel = 3 },
                            new CuentaContable { Codigo = "1.1.02", Nombre = "Caja Chica", Tipo = "Activo", Nivel = 3 },
                            new CuentaContable { Codigo = "1.1.03", Nombre = "Bancos Moneda Nacional", Tipo = "Activo", Nivel = 3 },
                            new CuentaContable { Codigo = "1.1.05", Nombre = "Cuentas por Cobrar Comerciales", Tipo = "Activo", Nivel = 3 },
                            new CuentaContable { Codigo = "1.1.06", Nombre = "Mercaderías / Inventarios", Tipo = "Activo", Nivel = 3 },
                            new CuentaContable { Codigo = "2", Nombre = "PASIVO", Tipo = "Pasivo", Nivel = 1 },
                            new CuentaContable { Codigo = "2.1", Nombre = "Pasivo Corriente", Tipo = "Pasivo", Nivel = 2 },
                            new CuentaContable { Codigo = "2.1.01", Nombre = "Tributos por Pagar (IGV / IVA)", Tipo = "Pasivo", Nivel = 3 },
                            new CuentaContable { Codigo = "2.1.03", Nombre = "Cuentas por Pagar Comerciales / Proveedores", Tipo = "Pasivo", Nivel = 3 },
                            new CuentaContable { Codigo = "3", Nombre = "PATRIMONIO", Tipo = "Patrimonio", Nivel = 1 },
                            new CuentaContable { Codigo = "3.1.01", Nombre = "Capital Social", Tipo = "Patrimonio", Nivel = 3 },
                            new CuentaContable { Codigo = "4", Nombre = "INGRESOS", Tipo = "Ingreso", Nivel = 1 },
                            new CuentaContable { Codigo = "4.1.01", Nombre = "Ventas de Mercaderías", Tipo = "Ingreso", Nivel = 3 },
                            new CuentaContable { Codigo = "4.1.02", Nombre = "Prestación de Servicios", Tipo = "Ingreso", Nivel = 3 },
                            new CuentaContable { Codigo = "5", Nombre = "GASTOS", Tipo = "Gasto", Nivel = 1 },
                            new CuentaContable { Codigo = "5.1.01", Nombre = "Costo de Ventas", Tipo = "Gasto", Nivel = 3 },
                            new CuentaContable { Codigo = "5.1.02", Nombre = "Gastos de Personal / Planilla", Tipo = "Gasto", Nivel = 3 },
                            new CuentaContable { Codigo = "5.1.03", Nombre = "Servicios Prestados por Terceros", Tipo = "Gasto", Nivel = 3 }
                        };
                        _context.InsertManyCuentasSimultaneo(catalogoBase);
                    }
                }

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();
            }
            catch (Exception ex)
            {
                ViewBag.SeedResult = "Error al crear datos semilla: " + ex.Message;
            }

            return View();
        }

        #region Helpers
        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl != "/")
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Dashboard");
        }
        #endregion
    }
}
