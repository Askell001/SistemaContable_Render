using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Filters;
using SistemaContable.Models;
using SistemaContable.Services;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador exclusivo para el Contador para la gestión del Plan / Catálogo de Cuentas Contables con validación de códigos únicos en MongoDB.
    /// </summary>
    [SessionAuthorize(Roles = "Contador")]
    public class PlanCuentasController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;
        private readonly NotificacionService _notificacionService = new NotificacionService();

        // GET: /PlanCuentas/Index
        public ActionResult Index(string search, string tipo)
        {
            if (!_context.IsConnected || _context.CuentasContables == null)
            {
                TempData["MensajeError"] = "No hay conexión con la base de datos de cuentas contables.";
                return View(new List<CuentaContable>());
            }

            try
            {
                var builder = Builders<CuentaContable>.Filter;
                var filter = builder.Empty;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var regexPattern = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                    var searchFilter = builder.Or(
                        builder.Regex(c => c.Codigo, regexPattern),
                        builder.Regex(c => c.Nombre, regexPattern)
                    );
                    filter = builder.And(filter, searchFilter);
                }

                if (!string.IsNullOrWhiteSpace(tipo))
                {
                    filter = builder.And(filter, builder.Eq(c => c.Tipo, tipo));
                }

                var cuentas = _context.CuentasContables
                    .Find(filter)
                    .SortBy(c => c.Codigo)
                    .ToList();

                ViewBag.Search = search;
                ViewBag.SelectedTipo = tipo;
                ViewBag.TotalCuentas = _context.CuentasContables.CountDocuments(FilterDefinition<CuentaContable>.Empty);

                return View(cuentas);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al consultar el catálogo de cuentas: " + ex.Message;
                return View(new List<CuentaContable>());
            }
        }

        // GET: /PlanCuentas/Create
        public ActionResult Create()
        {
            var model = new CuentaContable
            {
                Nivel = 3,
                Tipo = "Activo"
            };
            CargarTiposCuentaViewBag();
            return View(model);
        }

        // POST: /PlanCuentas/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CuentaContable model)
        {
            CargarTiposCuentaViewBag(model.Tipo);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                string codigoNormalizado = model.Codigo.Trim();

                // Validación estricta en C#: Evitar códigos de cuenta duplicados en MongoDB
                var existeCodigo = _context.CuentasContables
                    .Find(c => c.Codigo.ToLower() == codigoNormalizado.ToLower())
                    .FirstOrDefault();

                if (existeCodigo != null)
                {
                    ModelState.AddModelError("Codigo", $"El código de cuenta '{codigoNormalizado}' ya está registrado ({existeCodigo.Nombre}).");
                    return View(model);
                }

                model.Codigo = codigoNormalizado;
                model.Nombre = model.Nombre.Trim();

                _context.InsertCuentaSimultanea(model);

                // Notificación
                string usuarioId = Session["UsuarioId"]?.ToString();
                if (!string.IsNullOrEmpty(usuarioId))
                {
                    _notificacionService.CrearNotificacion(usuarioId, $"Se creó la cuenta contable {model.Codigo} - {model.Nombre}", "Exito");
                }

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"La cuenta '{model.Codigo} - {model.Nombre}' fue registrada exitosamente.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al registrar la cuenta contable: " + ex.Message);
                return View(model);
            }
        }

        // GET: /PlanCuentas/Edit/5
        public ActionResult Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var cuenta = _context.CuentasContables.Find(c => c.Id == id).FirstOrDefault();
                if (cuenta == null)
                {
                    TempData["MensajeError"] = "La cuenta contable solicitada no existe.";
                    return RedirectToAction("Index");
                }

                CargarTiposCuentaViewBag(cuenta.Tipo);
                return View(cuenta);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al cargar la cuenta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // POST: /PlanCuentas/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(CuentaContable model)
        {
            CargarTiposCuentaViewBag(model.Tipo);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                string codigoNormalizado = model.Codigo.Trim();

                // Validación estricta en C#: Verificar que el código no pertenezca a otra cuenta
                var codigoOcupado = _context.CuentasContables
                    .Find(c => c.Codigo.ToLower() == codigoNormalizado.ToLower() && c.Id != model.Id)
                    .FirstOrDefault();

                if (codigoOcupado != null)
                {
                    ModelState.AddModelError("Codigo", $"El código '{codigoNormalizado}' ya está en uso por la cuenta '{codigoOcupado.Nombre}'.");
                    return View(model);
                }

                var updateBuilder = Builders<CuentaContable>.Update
                    .Set(c => c.Codigo, codigoNormalizado)
                    .Set(c => c.Nombre, model.Nombre.Trim())
                    .Set(c => c.Tipo, model.Tipo)
                    .Set(c => c.Nivel, model.Nivel);

                _context.UpdateCuentaSimultanea(model.Id, updateBuilder);

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"Cuenta '{model.Codigo} - {model.Nombre}' actualizada correctamente.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al actualizar la cuenta en MongoDB: " + ex.Message);
                return View(model);
            }
        }

        // POST: /PlanCuentas/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var cuenta = _context.CuentasContables.Find(c => c.Id == id).FirstOrDefault();
                if (cuenta == null)
                {
                    TempData["MensajeError"] = "Cuenta contable no encontrada.";
                    return RedirectToAction("Index");
                }

                // Validación de integridad referencial: Verificar si tiene movimientos en Asientos Contables
                if (_context.AsientosContables != null)
                {
                    var tieneMovimientos = _context.AsientosContables
                        .Find(a => a.Detalles.Any(d => d.CuentaId == id))
                        .Any();

                    if (tieneMovimientos)
                    {
                        TempData["MensajeError"] = $"No se puede eliminar la cuenta '{cuenta.Codigo}' porque ya tiene movimientos registrados en asientos contables.";
                        return RedirectToAction("Index");
                    }
                }

                _context.DeleteCuentaSimultanea(id);

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"Cuenta contable '{cuenta.Codigo} - {cuenta.Nombre}' eliminada.";
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al eliminar cuenta contable: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: /PlanCuentas/SeedCatalogo
        [HttpGet]
        public ActionResult SeedCatalogo()
        {
            try
            {
                if (!_context.IsConnected)
                {
                    TempData["MensajeError"] = "No hay conexión con MongoDB.";
                    return RedirectToAction("Index");
                }

                long totalActual = _context.CuentasContables.CountDocuments(FilterDefinition<CuentaContable>.Empty);
                if (totalActual > 0)
                {
                    TempData["MensajeError"] = "El catálogo ya cuenta con cuentas registradas.";
                    return RedirectToAction("Index");
                }

                // Catálogo estándar inicial
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

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"Se insertaron {catalogoBase.Count} cuentas contables estándar en MongoDB (Atlas y Localhost).";
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al inicializar catálogo: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        #region Helpers
        private void CargarTiposCuentaViewBag(string selected = null)
        {
            var tipos = new List<string> { "Activo", "Pasivo", "Patrimonio", "Ingreso", "Gasto" };
            ViewBag.TiposDisponibles = tipos.Select(t => new SelectListItem
            {
                Value = t,
                Text = t,
                Selected = (t == selected)
            }).ToList();
        }
        #endregion
    }
}
