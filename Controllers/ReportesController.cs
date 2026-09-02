using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Filters;
using SistemaContable.Models;
using SistemaContable.Services;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador para la consulta, filtrado y exportación de reportes de Asientos Contables
    /// en formatos Excel, PDF y XML con guardado dual (Escritorio/Reportes y Descarga).
    /// </summary>
    [SessionAuthorize]
    public class ReportesController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;
        private readonly ReporteService _reporteService = new ReporteService();

        // GET: /Reportes/Index
        public ActionResult Index(DateTime? fechaInicio, DateTime? fechaFin, string estado = "Todos")
        {
            var model = ConsultarDatosReporte(fechaInicio, fechaFin, estado);
            return View(model);
        }

        // POST: /Reportes/Exportar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Exportar(DateTime? fechaInicio, DateTime? fechaFin, string estado = "Todos", string formato = "excel")
        {
            try
            {
                var model = ConsultarDatosReporte(fechaInicio, fechaFin, estado);

                byte[] contenido;
                string extension;
                string mimeType;

                switch (formato?.ToLower())
                {
                    case "pdf":
                        contenido = _reporteService.GenerarPdf(model);
                        extension = "pdf";
                        mimeType = "application/pdf";
                        break;

                    case "xml":
                        contenido = _reporteService.GenerarXml(model);
                        extension = "xml";
                        mimeType = "application/xml";
                        break;

                    case "excel":
                    default:
                        contenido = _reporteService.GenerarExcel(model);
                        extension = "xlsx";
                        mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        break;
                }

                var fechaEC = ReporteService.ObtenerHoraEcuador();
                string nombreArchivo = $"ReporteContable_{fechaEC:yyyyMMdd_HHmmss}.{extension}";

                // Retornar archivo binario directamente como descarga al navegador del usuario
                return File(contenido, mimeType, nombreArchivo);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al generar el reporte: " + ex.Message;
                return RedirectToAction("Index", new { fechaInicio, fechaFin, estado });
            }
        }

        #region Helpers de Consulta
        private FiltroReporteViewModel ConsultarDatosReporte(DateTime? fechaInicio, DateTime? fechaFin, string estado)
        {
            var fechaEC = ReporteService.ObtenerHoraEcuador();
            string nombreUsuario = Session["Nombre"]?.ToString() ?? "Usuario Sistema";
            string correoUsuario = Session["UsuarioId"]?.ToString() ?? "usuario@contable.com";

            // Intentar recuperar correo real del usuario
            if (_context.IsConnected && _context.Usuarios != null && Session["UsuarioId"] != null)
            {
                try
                {
                    string uid = Session["UsuarioId"].ToString();
                    var u = _context.Usuarios.Find(x => x.Id == uid).FirstOrDefault();
                    if (u != null)
                    {
                        nombreUsuario = u.Nombre;
                        correoUsuario = u.Correo;
                    }
                }
                catch { }
            }

            var model = new FiltroReporteViewModel
            {
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                Estado = string.IsNullOrEmpty(estado) ? "Todos" : estado,
                FechaGeneracionEC = fechaEC,
                UsuarioNombre = nombreUsuario,
                UsuarioCorreo = correoUsuario,
                Empresa = Session["Empresa"]?.ToString() ?? "Empresa Principal S.A.",
                Asientos = new List<ReporteAsientoItemDto>()
            };

            if (!_context.IsConnected || _context.AsientosContables == null)
            {
                return model;
            }

            try
            {
                string userRol = Session["Rol"]?.ToString() ?? "";
                string userEmpresa = Session["Empresa"]?.ToString() ?? "Empresa Principal S.A.";

                // Diccionario de cuentas para nombres rápidos
                var dictCuentas = new Dictionary<string, CuentaContable>();
                if (_context.CuentasContables != null)
                {
                    var listaCuentas = _context.CuentasContables.Find(FilterDefinition<CuentaContable>.Empty).ToList();
                    foreach (var c in listaCuentas)
                    {
                        if (!dictCuentas.ContainsKey(c.Id)) dictCuentas[c.Id] = c;
                    }
                }

                // Diccionario de usuarios
                var dictUsuarios = new Dictionary<string, Usuario>();
                if (_context.Usuarios != null)
                {
                    var listaUsuarios = _context.Usuarios.Find(FilterDefinition<Usuario>.Empty).ToList();
                    foreach (var u in listaUsuarios)
                    {
                        if (!dictUsuarios.ContainsKey(u.Id)) dictUsuarios[u.Id] = u;
                    }
                }

                // Construcción de Filtro MongoDB
                var builder = Builders<AsientoContable>.Filter;
                var filter = builder.Empty;

                // El Lector SOLO puede ver los reportes asociados a su empresa
                if (string.Equals(userRol, "Lectura", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(userRol, "Lector", StringComparison.OrdinalIgnoreCase))
                {
                    var companyFilter = builder.Or(
                        builder.Eq(a => a.Empresa, userEmpresa),
                        builder.Eq(a => a.Empresa, null),
                        builder.Eq(a => a.Empresa, "")
                    );
                    filter = builder.And(filter, companyFilter);
                }

                if (fechaInicio.HasValue)
                {
                    var fIniUtc = fechaInicio.Value.Date.ToUniversalTime();
                    filter = builder.And(filter, builder.Gte(a => a.Fecha, fIniUtc));
                }

                if (fechaFin.HasValue)
                {
                    var fFinUtc = fechaFin.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
                    filter = builder.And(filter, builder.Lte(a => a.Fecha, fFinUtc));
                }

                if (!string.IsNullOrEmpty(estado) && estado != "Todos")
                {
                    filter = builder.And(filter, builder.Eq(a => a.Estado, estado));
                }

                var asientosDb = _context.AsientosContables
                    .Find(filter)
                    .SortByDescending(a => a.NumeroAsiento)
                    .ToList();

                foreach (var a in asientosDb)
                {
                    string uNombre = "Sistema";
                    string uCorreo = "sistema@contable.com";
                    if (!string.IsNullOrEmpty(a.UsuarioId) && dictUsuarios.ContainsKey(a.UsuarioId))
                    {
                        uNombre = dictUsuarios[a.UsuarioId].Nombre;
                        uCorreo = dictUsuarios[a.UsuarioId].Correo;
                    }

                    var itemDto = new ReporteAsientoItemDto
                    {
                        Id = a.Id,
                        NumeroAsiento = a.NumeroAsiento,
                        Fecha = a.Fecha.ToLocalTime(),
                        Concepto = a.Concepto,
                        Estado = a.Estado,
                        UsuarioCreadorNombre = uNombre,
                        UsuarioCreadorCorreo = uCorreo,
                        TotalDebe = a.Detalles != null ? a.Detalles.Sum(d => d.Debe) : 0m,
                        TotalHaber = a.Detalles != null ? a.Detalles.Sum(d => d.Haber) : 0m,
                        Detalles = new List<ReporteDetalleItemDto>()
                    };

                    if (a.Detalles != null)
                    {
                        foreach (var d in a.Detalles)
                        {
                            string cCod = "S/C";
                            string cNom = "Cuenta Desconocida";
                            string cTip = "General";

                            if (!string.IsNullOrEmpty(d.CuentaId) && dictCuentas.ContainsKey(d.CuentaId))
                            {
                                cCod = dictCuentas[d.CuentaId].Codigo;
                                cNom = dictCuentas[d.CuentaId].Nombre;
                                cTip = dictCuentas[d.CuentaId].Tipo;
                            }

                            itemDto.Detalles.Add(new ReporteDetalleItemDto
                            {
                                CuentaCodigo = cCod,
                                CuentaNombre = cNom,
                                CuentaTipo = cTip,
                                Debe = d.Debe,
                                Haber = d.Haber
                            });
                        }
                    }

                    model.Asientos.Add(itemDto);
                }
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al consultar los asientos para el reporte: " + ex.Message;
            }

            return model;
        }
        #endregion
    }
}
