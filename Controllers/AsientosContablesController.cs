using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Controlador central exclusivo para el Contador para la gestión de pólizas y asientos por partida doble.
    /// Valida estrictamente en Backend que la suma del Debe y del Haber sea idéntica (Diferencia = 0).
    /// </summary>
    [SessionAuthorize(Roles = "Contador")]
    public class AsientosContablesController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;
        private readonly NotificacionService _notificacionService = new NotificacionService();

        // GET: /AsientosContables/Index
        public ActionResult Index(string search, string estado, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!_context.IsConnected || _context.AsientosContables == null)
            {
                TempData["MensajeError"] = "No hay conexión con la base de datos de asientos contables.";
                return View(new List<AsientoComprobanteDto>());
            }

            try
            {
                var builder = Builders<AsientoContable>.Filter;
                var filter = builder.Empty;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    if (int.TryParse(search.Trim(), out int num))
                    {
                        filter = builder.And(filter, builder.Eq(a => a.NumeroAsiento, num));
                    }
                    else
                    {
                        filter = builder.And(filter, builder.Regex(a => a.Concepto, new MongoDB.Bson.BsonRegularExpression(search.Trim(), "i")));
                    }
                }

                if (!string.IsNullOrWhiteSpace(estado))
                {
                    filter = builder.And(filter, builder.Eq(a => a.Estado, estado));
                }

                if (fechaInicio.HasValue)
                {
                    filter = builder.And(filter, builder.Gte(a => a.Fecha, fechaInicio.Value.Date));
                }

                if (fechaFin.HasValue)
                {
                    filter = builder.And(filter, builder.Lte(a => a.Fecha, fechaFin.Value.Date.AddDays(1).AddTicks(-1)));
                }

                var asientos = _context.AsientosContables
                    .Find(filter)
                    .SortByDescending(a => a.NumeroAsiento)
                    .ToList();

                // Cargar usuarios para resolver nombres
                var usuariosDict = _context.Usuarios != null 
                    ? _context.Usuarios.Find(FilterDefinition<Usuario>.Empty).ToList().ToDictionary(u => u.Id, u => u.Nombre)
                    : new Dictionary<string, string>();

                // Cargar catálogo de cuentas para nombres
                var cuentasDict = _context.CuentasContables != null
                    ? _context.CuentasContables.Find(FilterDefinition<CuentaContable>.Empty).ToList().ToDictionary(c => c.Id, c => c)
                    : new Dictionary<string, CuentaContable>();

                var itemsDto = asientos.Select(a => new AsientoComprobanteDto
                {
                    Id = a.Id,
                    NumeroAsiento = a.NumeroAsiento,
                    Fecha = a.Fecha,
                    Concepto = a.Concepto,
                    UsuarioId = a.UsuarioId,
                    NombreUsuario = (!string.IsNullOrEmpty(a.UsuarioId) && usuariosDict.ContainsKey(a.UsuarioId)) ? usuariosDict[a.UsuarioId] : "Sistema",
                    Empresa = !string.IsNullOrEmpty(a.Empresa) ? a.Empresa : "Empresa Principal S.A.",
                    Estado = a.Estado,
                    TotalDebe = a.Detalles != null ? a.Detalles.Sum(d => d.Debe) : 0m,
                    TotalHaber = a.Detalles != null ? a.Detalles.Sum(d => d.Haber) : 0m,
                    Lineas = a.Detalles != null ? a.Detalles.Select(d => new DetalleLineaDto
                    {
                        CuentaId = d.CuentaId,
                        CodigoCuenta = cuentasDict.ContainsKey(d.CuentaId) ? cuentasDict[d.CuentaId].Codigo : "S/C",
                        NombreCuenta = cuentasDict.ContainsKey(d.CuentaId) ? cuentasDict[d.CuentaId].Nombre : "Cuenta Desconocida",
                        Debe = d.Debe,
                        Haber = d.Haber
                    }).ToList() : new List<DetalleLineaDto>()
                }).ToList();

                ViewBag.Search = search;
                ViewBag.SelectedEstado = estado;
                ViewBag.FechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
                ViewBag.FechaFin = fechaFin?.ToString("yyyy-MM-dd");

                return View(itemsDto);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al listar los asientos contables: " + ex.Message;
                return View(new List<AsientoComprobanteDto>());
            }
        }

        // GET: /AsientosContables/NuevoAsiento
        public ActionResult NuevoAsiento()
        {
            // Si no existen cuentas contables en la base de datos, auto-semillar catálogo base de forma segura
            try
            {
                if (_context.IsConnected && _context.CuentasContables != null)
                {
                    long totalCuentas = _context.CuentasContables.CountDocuments(FilterDefinition<CuentaContable>.Empty);
                    if (totalCuentas == 0)
                    {
                        AutoSemillarCatalogoBase();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[AsientosContablesController] Aviso en verificación de cuentas: {ex.Message}");
            }

            var model = new AsientoFormViewModel
            {
                Fecha = DateTime.Today,
                NumeroAsiento = ObtenerSiguienteNumeroAsiento(),
                Tipo = "Ingreso",
                Estado = "Aprobado",
                Empresa = Session["Empresa"]?.ToString() ?? "Empresa Principal S.A.",
                CuentasDisponibles = ObtenerCuentasSelectList(),
                Detalles = new List<DetalleAsientoFormModel>
                {
                    new DetalleAsientoFormModel { Debe = 0m, Haber = 0m },
                    new DetalleAsientoFormModel { Debe = 0m, Haber = 0m }
                }
            };

            CargarCuentasJsonViewBag();

            return View(model);
        }

        // POST: /AsientosContables/NuevoAsiento
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult NuevoAsiento(AsientoFormViewModel model)
        {
            model.CuentasDisponibles = ObtenerCuentasSelectList();

            // 1. Filtrar líneas válidas (que tengan cuenta asignada)
            var lineasValidas = model.Detalles != null 
                ? model.Detalles.Where(d => !string.IsNullOrWhiteSpace(d.CuentaId)).ToList() 
                : new List<DetalleAsientoFormModel>();

            if (lineasValidas.Count < 2)
            {
                ModelState.AddModelError("", "Un asiento contable por partida doble requiere al menos 2 cuentas con movimientos (Debe y Haber).");
            }

            // 2. Validación Estricta de Partida Doble en Backend (Math.Abs(sumaDebe - sumaHaber) < 0.001m)
            decimal totalDebe = lineasValidas.Sum(d => d.Debe);
            decimal totalHaber = lineasValidas.Sum(d => d.Haber);
            decimal diferencia = Math.Abs(totalDebe - totalHaber);

            if (diferencia >= 0.001m)
            {
                ModelState.AddModelError("", $"Descuadre contable detectado. El Debe (${totalDebe:N2}) y el Haber (${totalHaber:N2}) deben ser exactamente iguales. Diferencia: ${diferencia:N2}.");
            }

            if (totalDebe <= 0m || totalHaber <= 0m)
            {
                ModelState.AddModelError("", "Los montos totales del asiento contable deben ser mayores a $0.00.");
            }

            if (!ModelState.IsValid)
            {
                CargarCuentasJsonViewBag();
                return View(model);
            }

            try
            {
                string usuarioId = Session["UsuarioId"]?.ToString() ?? "000000000000000000000000";
                string empresaAsignada = !string.IsNullOrEmpty(model.Empresa) ? model.Empresa : (Session["Empresa"]?.ToString() ?? "Empresa Principal S.A.");

                var nuevoAsiento = new AsientoContable
                {
                    NumeroAsiento = ObtenerSiguienteNumeroAsiento(),
                    Fecha = model.Fecha,
                    Concepto = model.Concepto.Trim(),
                    UsuarioId = usuarioId,
                    Empresa = empresaAsignada,
                    Estado = model.Estado ?? "Aprobado",
                    Detalles = lineasValidas.Select(d => new DetalleAsientoContable
                    {
                        CuentaId = d.CuentaId,
                        Debe = d.Debe,
                        Haber = d.Haber
                    }).ToList()
                };

                _context.InsertAsientoSimultaneo(nuevoAsiento);

                // Notificación automática al usuario
                if (!string.IsNullOrEmpty(usuarioId))
                {
                    _notificacionService.CrearNotificacion(
                        usuarioId, 
                        $"Asiento Contable #{nuevoAsiento.NumeroAsiento} registrado con éxito ({nuevoAsiento.Estado}). Total: {totalDebe:N2}", 
                        "Exito");
                }

                // Ejecutar Respaldo Integral Completo No Bloqueante (JSON en Escritorio y MongoDB Local)
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"Asiento Contable #{nuevoAsiento.NumeroAsiento} registrado exitosamente con Partida Doble balanceada ({nuevoAsiento.Estado}).";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al registrar el asiento en MongoDB: " + ex.Message);
                return View(model);
            }
        }

        // GET: /AsientosContables/Detalle/5
        public ActionResult Detalle(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var asiento = _context.AsientosContables.Find(a => a.Id == id).FirstOrDefault();
                if (asiento == null)
                {
                    TempData["MensajeError"] = "Asiento contable no encontrado.";
                    return RedirectToAction("Index");
                }

                // Cargar catálogo de cuentas para mapear código y nombre
                var cuentasDict = _context.CuentasContables != null
                    ? _context.CuentasContables.Find(FilterDefinition<CuentaContable>.Empty).ToList().ToDictionary(c => c.Id, c => c)
                    : new Dictionary<string, CuentaContable>();

                // Cargar usuario autor
                string nombreUsuario = "Sistema";
                if (!string.IsNullOrEmpty(asiento.UsuarioId) && _context.Usuarios != null)
                {
                    var user = _context.Usuarios.Find(u => u.Id == asiento.UsuarioId).FirstOrDefault();
                    if (user != null) nombreUsuario = user.Nombre;
                }

                var dto = new AsientoComprobanteDto
                {
                    Id = asiento.Id,
                    NumeroAsiento = asiento.NumeroAsiento,
                    Fecha = asiento.Fecha,
                    Concepto = asiento.Concepto,
                    UsuarioId = asiento.UsuarioId,
                    NombreUsuario = nombreUsuario,
                    Estado = asiento.Estado,
                    TotalDebe = asiento.Detalles != null ? asiento.Detalles.Sum(d => d.Debe) : 0m,
                    TotalHaber = asiento.Detalles != null ? asiento.Detalles.Sum(d => d.Haber) : 0m,
                    Lineas = asiento.Detalles != null ? asiento.Detalles.Select(d => new DetalleLineaDto
                    {
                        CuentaId = d.CuentaId,
                        CodigoCuenta = (cuentasDict.ContainsKey(d.CuentaId)) ? cuentasDict[d.CuentaId].Codigo : "---",
                        NombreCuenta = (cuentasDict.ContainsKey(d.CuentaId)) ? cuentasDict[d.CuentaId].Nombre : "Cuenta no encontrada",
                        Debe = d.Debe,
                        Haber = d.Haber
                    }).ToList() : new List<DetalleLineaDto>()
                };

                return View(dto);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al cargar detalle del asiento: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // POST: /AsientosContables/Anular/5 (Anulación contable para mantener auditoría en MongoDB)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Anular(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var asiento = _context.AsientosContables.Find(a => a.Id == id).FirstOrDefault();
                if (asiento == null)
                {
                    TempData["MensajeError"] = "Asiento contable no encontrado.";
                    return RedirectToAction("Index");
                }

                if (asiento.Estado == "Anulado")
                {
                    TempData["MensajeError"] = $"El asiento #{asiento.NumeroAsiento} ya se encuentra anulado.";
                    return RedirectToAction("Index");
                }

                // Cambiar estado a 'Anulado' sin borrar el documento de MongoDB
                _context.UpdateAsientoSimultaneo(
                    id,
                    Builders<AsientoContable>.Update.Set(a => a.Estado, "Anulado")
                );

                string usuarioId = Session["UsuarioId"]?.ToString();
                if (!string.IsNullOrEmpty(usuarioId))
                {
                    _notificacionService.CrearNotificacion(
                        usuarioId, 
                        $"El Asiento Contable #{asiento.NumeroAsiento} fue ANULADO por auditoría.", 
                        "Alerta");
                }

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"El Asiento Contable #{asiento.NumeroAsiento} fue anulado correctamente. El registro se conserva para fines de auditoría.";
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al anular asiento contable: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: /AsientosContables/Aprobar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Aprobar(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                _context.UpdateAsientoSimultaneo(
                    id,
                    Builders<AsientoContable>.Update.Set(a => a.Estado, "Aprobado")
                );

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = "Asiento contable aprobado correctamente.";
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al aprobar asiento: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        #region Helpers
        private int ObtenerSiguienteNumeroAsiento()
        {
            try
            {
                if (_context.IsConnected && _context.AsientosContables != null)
                {
                    var ultimoAsiento = _context.AsientosContables
                        .Find(FilterDefinition<AsientoContable>.Empty)
                        .SortByDescending(a => a.NumeroAsiento)
                        .Limit(1)
                        .FirstOrDefault();

                    if (ultimoAsiento != null)
                    {
                        return ultimoAsiento.NumeroAsiento + 1;
                    }
                }
            }
            catch { }
            return 1;
        }

        private IEnumerable<SelectListItem> ObtenerCuentasSelectList()
        {
            var items = new List<SelectListItem>();
            try
            {
                if (_context.IsConnected && _context.CuentasContables != null)
                {
                    long total = _context.CuentasContables.CountDocuments(FilterDefinition<CuentaContable>.Empty);
                    if (total == 0)
                    {
                        AutoSemillarCatalogoBase();
                    }

                    var cuentas = _context.CuentasContables
                        .Find(FilterDefinition<CuentaContable>.Empty)
                        .SortBy(c => c.Codigo)
                        .ToList();

                    foreach (var c in cuentas)
                    {
                        items.Add(new SelectListItem
                        {
                            Value = c.Id,
                            Text = $"{c.Codigo} - {c.Nombre} ({c.Tipo})"
                        });
                    }
                }
            }
            catch { }
            return items;
        }

        private void CargarCuentasJsonViewBag()
        {
            try
            {
                if (_context.IsConnected && _context.CuentasContables != null)
                {
                    var cuentas = _context.CuentasContables
                        .Find(FilterDefinition<CuentaContable>.Empty)
                        .SortBy(c => c.Codigo)
                        .ToList();

                    var listaJson = cuentas.Select(c => new
                    {
                        id = c.Id,
                        codigo = c.Codigo,
                        nombre = c.Nombre,
                        tipo = c.Tipo,
                        nivel = c.Nivel
                    });

                    ViewBag.CuentasJson = System.Text.Json.JsonSerializer.Serialize(listaJson);
                }
                else
                {
                    ViewBag.CuentasJson = "[]";
                }
            }
            catch
            {
                ViewBag.CuentasJson = "[]";
            }
        }

        private void AutoSemillarCatalogoBase()
        {
            try
            {
                var catalogoBase = new List<CuentaContable>
                {
                    // 1. ACTIVOS
                    new CuentaContable { Codigo = "1", Nombre = "ACTIVO", Tipo = "Activo", Nivel = 1 },
                    new CuentaContable { Codigo = "1.1", Nombre = "Activo Corriente", Tipo = "Activo", Nivel = 2 },
                    new CuentaContable { Codigo = "1.1.01", Nombre = "Caja General", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.1.02", Nombre = "Caja Chica", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.1.03", Nombre = "Bancos Moneda Nacional", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.1.04", Nombre = "Bancos Moneda Extranjera", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.1.05", Nombre = "Cuentas por Cobrar Comerciales", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.1.06", Nombre = "Mercaderías / Inventarios", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.2", Nombre = "Activo No Corriente", Tipo = "Activo", Nivel = 2 },
                    new CuentaContable { Codigo = "1.2.01", Nombre = "Propiedad, Planta y Equipo", Tipo = "Activo", Nivel = 3 },
                    new CuentaContable { Codigo = "1.2.02", Nombre = "Depreciación Acumulada", Tipo = "Activo", Nivel = 3 },

                    // 2. PASIVOS
                    new CuentaContable { Codigo = "2", Nombre = "PASIVO", Tipo = "Pasivo", Nivel = 1 },
                    new CuentaContable { Codigo = "2.1", Nombre = "Pasivo Corriente", Tipo = "Pasivo", Nivel = 2 },
                    new CuentaContable { Codigo = "2.1.01", Nombre = "Tributos por Pagar (IGV / IVA)", Tipo = "Pasivo", Nivel = 3 },
                    new CuentaContable { Codigo = "2.1.02", Nombre = "Remuneraciones por Pagar", Tipo = "Pasivo", Nivel = 3 },
                    new CuentaContable { Codigo = "2.1.03", Nombre = "Cuentas por Pagar Comerciales / Proveedores", Tipo = "Pasivo", Nivel = 3 },
                    new CuentaContable { Codigo = "2.1.04", Nombre = "Préstamos Bancarios Corto Plazo", Tipo = "Pasivo", Nivel = 3 },

                    // 3. PATRIMONIO
                    new CuentaContable { Codigo = "3", Nombre = "PATRIMONIO", Tipo = "Patrimonio", Nivel = 1 },
                    new CuentaContable { Codigo = "3.1", Nombre = "Capital y Reservas", Tipo = "Patrimonio", Nivel = 2 },
                    new CuentaContable { Codigo = "3.1.01", Nombre = "Capital Social", Tipo = "Patrimonio", Nivel = 3 },
                    new CuentaContable { Codigo = "3.1.02", Nombre = "Reserva Legal", Tipo = "Patrimonio", Nivel = 3 },
                    new CuentaContable { Codigo = "3.1.03", Nombre = "Resultados Acumulados", Tipo = "Patrimonio", Nivel = 3 },

                    // 4. INGRESOS
                    new CuentaContable { Codigo = "4", Nombre = "INGRESOS", Tipo = "Ingreso", Nivel = 1 },
                    new CuentaContable { Codigo = "4.1", Nombre = "Ingresos Operacionales", Tipo = "Ingreso", Nivel = 2 },
                    new CuentaContable { Codigo = "4.1.01", Nombre = "Ventas de Mercaderías", Tipo = "Ingreso", Nivel = 3 },
                    new CuentaContable { Codigo = "4.1.02", Nombre = "Prestación de Servicios", Tipo = "Ingreso", Nivel = 3 },
                    new CuentaContable { Codigo = "4.2.01", Nombre = "Ingresos Financieros / Intereses", Tipo = "Ingreso", Nivel = 3 },

                    // 5. GASTOS
                    new CuentaContable { Codigo = "5", Nombre = "GASTOS", Tipo = "Gasto", Nivel = 1 },
                    new CuentaContable { Codigo = "5.1", Nombre = "Gastos Operativos", Tipo = "Gasto", Nivel = 2 },
                    new CuentaContable { Codigo = "5.1.01", Nombre = "Costo de Ventas", Tipo = "Gasto", Nivel = 3 },
                    new CuentaContable { Codigo = "5.1.02", Nombre = "Gastos de Personal / Planilla", Tipo = "Gasto", Nivel = 3 },
                    new CuentaContable { Codigo = "5.1.03", Nombre = "Servicios Prestados por Terceros", Tipo = "Gasto", Nivel = 3 },
                    new CuentaContable { Codigo = "5.1.04", Nombre = "Gastos Financieros / Comisiones Bancarias", Tipo = "Gasto", Nivel = 3 }
                };

                _context.InsertManyCuentasSimultaneo(catalogoBase);
            }
            catch { }
        }
        #endregion
    }
}
