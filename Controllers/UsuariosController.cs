using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using BCrypt.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Filters;
using SistemaContable.Models;

namespace SistemaContable.Controllers
{
    /// <summary>
    /// Controlador CRUD exclusivo para el Administrador para la gestión de usuarios y empresas.
    /// </summary>
    [SessionAuthorize(Roles = "Admin")]
    public class UsuariosController : Controller
    {
        private readonly MongoDbContext _context = MongoDbContext.Instance;
        private const int PageSize = 8;

        // GET: /Usuarios/Index
        public ActionResult Index(string search, string rolId, bool? estado, int page = 1)
        {
            if (!_context.IsConnected || _context.Usuarios == null)
            {
                TempData["MensajeError"] = "Error de conexión con MongoDB. Verifique su base de datos.";
                return View(new List<UsuarioItemDto>());
            }

            try
            {
                // 1. Construir filtros dinámicos de MongoDB
                var builder = Builders<Usuario>.Filter;
                var filter = builder.Empty;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var regexPattern = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                    var nameOrEmailFilter = builder.Or(
                        builder.Regex(u => u.Nombre, regexPattern),
                        builder.Regex(u => u.Correo, regexPattern),
                        builder.Regex(u => u.Empresa, regexPattern)
                    );
                    filter = builder.And(filter, nameOrEmailFilter);
                }

                if (!string.IsNullOrWhiteSpace(rolId))
                {
                    filter = builder.And(filter, builder.Eq(u => u.RolId, rolId));
                }

                if (estado.HasValue)
                {
                    filter = builder.And(filter, builder.Eq(u => u.Estado, estado.Value));
                }

                // 2. Conteo total para paginación
                long totalRegistros = _context.Usuarios.CountDocuments(filter);
                int totalPaginas = (int)Math.Ceiling((double)totalRegistros / PageSize);
                if (totalPaginas == 0) totalPaginas = 1;
                if (page < 1) page = 1;
                if (page > totalPaginas) page = totalPaginas;

                // 3. Consulta paginada
                var usuarios = _context.Usuarios
                    .Find(filter)
                    .SortByDescending(u => u.FechaCreacion)
                    .Skip((page - 1) * PageSize)
                    .Limit(PageSize)
                    .ToList();

                // 4. Obtener diccionario de roles para mapear nombres
                var rolesDict = _context.Roles != null
                    ? _context.Roles.Find(FilterDefinition<Rol>.Empty).ToList().ToDictionary(r => r.Id, r => r.NombreRol)
                    : new Dictionary<string, string>();

                var itemsDto = usuarios.Select(u => new UsuarioItemDto
                {
                    Id = u.Id,
                    Nombre = u.Nombre,
                    Correo = u.Correo,
                    RolId = u.RolId,
                    NombreRol = (!string.IsNullOrEmpty(u.RolId) && rolesDict.ContainsKey(u.RolId)) ? rolesDict[u.RolId] : "Sin Asignar",
                    Empresa = !string.IsNullOrEmpty(u.Empresa) ? u.Empresa : "Empresa Principal S.A.",
                    Estado = u.Estado,
                    FechaCreacion = u.FechaCreacion
                }).ToList();

                // 5. ViewBag con metadatos de filtro y paginación
                ViewBag.Search = search;
                ViewBag.SelectedRolId = rolId;
                ViewBag.SelectedEstado = estado;
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPaginas;
                ViewBag.TotalRecords = totalRegistros;
                ViewBag.RolesList = GetRolesSelectList(rolId);

                return View(itemsDto);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al consultar usuarios: " + ex.Message;
                return View(new List<UsuarioItemDto>());
            }
        }

        // GET: /Usuarios/Create
        public ActionResult Create()
        {
            var model = new UsuarioFormViewModel
            {
                Estado = true,
                Empresa = "Empresa Principal S.A.",
                RolesDisponibles = GetRolesSelectList()
            };
            return View(model);
        }

        // POST: /Usuarios/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(UsuarioFormViewModel model)
        {
            model.RolesDisponibles = GetRolesSelectList(model.RolId);

            // Validar que en creación la contraseña sea requerida
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError("Password", "La contraseña es obligatoria para nuevos usuarios.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                string correoNormalizado = model.Correo.Trim().ToLowerInvariant();

                // Validar que el correo no esté duplicado
                var existe = _context.Usuarios.Find(u => u.Correo.ToLower() == correoNormalizado).FirstOrDefault();
                if (existe != null)
                {
                    ModelState.AddModelError("Correo", "Ya existe un usuario registrado con este correo electrónico.");
                    return View(model);
                }

                // Generar hash seguro con BCrypt
                string hashPassword = BCrypt.Net.BCrypt.HashPassword(model.Password);

                var nuevoUsuario = new Usuario
                {
                    Nombre = model.Nombre.Trim(),
                    Correo = correoNormalizado,
                    PasswordHash = hashPassword,
                    RolId = model.RolId,
                    Empresa = !string.IsNullOrWhiteSpace(model.Empresa) ? model.Empresa.Trim() : "Empresa Principal S.A.",
                    Estado = model.Estado,
                    FechaCreacion = DateTime.UtcNow
                };

                _context.InsertUsuarioSimultaneo(nuevoUsuario);

                // Registrar notificación de bienvenida en MongoDB para el nuevo usuario
                try
                {
                    var notifService = new Services.NotificacionService();
                    notifService.CrearNotificacion(
                        nuevoUsuario.Id, 
                        $"¡Bienvenido a ContaCloud! Tu cuenta ha sido creada exitosamente con rol asignado.", 
                        "Exito");
                }
                catch { }

                // Ejecutar Respaldo Integral Completo No Bloqueante (JSON en Escritorio y MongoDB Local)
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"El usuario '{nuevoUsuario.Nombre}' fue creado con éxito para la empresa '{nuevoUsuario.Empresa}'.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al crear el usuario en MongoDB: " + ex.Message);
                return View(model);
            }
        }

        // GET: /Usuarios/Edit/5
        public ActionResult Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return RedirectToAction("Index");
            }

            try
            {
                var usuario = _context.Usuarios.Find(u => u.Id == id).FirstOrDefault();
                if (usuario == null)
                {
                    TempData["MensajeError"] = "El usuario solicitado no existe.";
                    return RedirectToAction("Index");
                }

                var model = new UsuarioFormViewModel
                {
                    Id = usuario.Id,
                    Nombre = usuario.Nombre,
                    Correo = usuario.Correo,
                    RolId = usuario.RolId,
                    Empresa = !string.IsNullOrEmpty(usuario.Empresa) ? usuario.Empresa : "Empresa Principal S.A.",
                    Estado = usuario.Estado,
                    RolesDisponibles = GetRolesSelectList(usuario.RolId)
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al cargar usuario: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // POST: /Usuarios/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(UsuarioFormViewModel model)
        {
            model.RolesDisponibles = GetRolesSelectList(model.RolId);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var usuarioExistente = _context.Usuarios.Find(u => u.Id == model.Id).FirstOrDefault();
                if (usuarioExistente == null)
                {
                    TempData["MensajeError"] = "El usuario a modificar no existe.";
                    return RedirectToAction("Index");
                }

                string correoNormalizado = model.Correo.Trim().ToLowerInvariant();

                // Validar que el correo no esté ocupado por otro usuario
                var correoOcupado = _context.Usuarios.Find(u => u.Correo.ToLower() == correoNormalizado && u.Id != model.Id).FirstOrDefault();
                if (correoOcupado != null)
                {
                    ModelState.AddModelError("Correo", "El correo especificado ya pertenece a otro usuario.");
                    return View(model);
                }

                // Construir actualización para MongoDB
                var updateBuilder = Builders<Usuario>.Update
                    .Set(u => u.Nombre, model.Nombre.Trim())
                    .Set(u => u.Correo, correoNormalizado)
                    .Set(u => u.RolId, model.RolId)
                    .Set(u => u.Empresa, !string.IsNullOrWhiteSpace(model.Empresa) ? model.Empresa.Trim() : "Empresa Principal S.A.")
                    .Set(u => u.Estado, model.Estado);

                // Si se proporcionó una nueva contraseña, hashear con BCrypt y actualizar
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    string nuevoHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                    updateBuilder = updateBuilder.Set(u => u.PasswordHash, nuevoHash);
                }
                
                _context.UpdateUsuarioSimultaneo(model.Id, updateBuilder);

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = $"Usuario '{model.Nombre}' actualizado correctamente.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al actualizar el usuario en MongoDB: " + ex.Message);
                return View(model);
            }
        }

        // POST: /Usuarios/ToggleEstado/5 (Eliminación Lógica rápida)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleEstado(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var usuario = _context.Usuarios.Find(u => u.Id == id).FirstOrDefault();
                if (usuario != null)
                {
                    bool nuevoEstado = !usuario.Estado;
                    _context.UpdateUsuarioSimultaneo(
                        id,
                        Builders<Usuario>.Update.Set(u => u.Estado, nuevoEstado)
                    );

                    // Ejecutar Respaldo Integral Completo No Bloqueante
                    Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                    string accionTexto = nuevoEstado ? "reactivado" : "desactivado (eliminación lógica)";
                    TempData["MensajeExito"] = $"El usuario '{usuario.Nombre}' fue {accionTexto} correctamente.";
                }
                else
                {
                    TempData["MensajeError"] = "Usuario no encontrado.";
                }
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al cambiar estado del usuario: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: /Usuarios/Delete/5 (Vista de Confirmación de Eliminación Lógica)
        public ActionResult Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                var usuario = _context.Usuarios.Find(u => u.Id == id).FirstOrDefault();
                if (usuario == null)
                {
                    TempData["MensajeError"] = "Usuario no encontrado.";
                    return RedirectToAction("Index");
                }

                string nombreRol = "Sin Rol";
                if (!string.IsNullOrEmpty(usuario.RolId))
                {
                    var rol = _context.Roles.Find(r => r.Id == usuario.RolId).FirstOrDefault();
                    if (rol != null) nombreRol = rol.NombreRol;
                }

                var dto = new UsuarioItemDto
                {
                    Id = usuario.Id,
                    Nombre = usuario.Nombre,
                    Correo = usuario.Correo,
                    RolId = usuario.RolId,
                    NombreRol = nombreRol,
                    Estado = usuario.Estado,
                    FechaCreacion = usuario.FechaCreacion
                };

                return View(dto);
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al cargar usuario: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // POST: /Usuarios/Delete/5 (Confirmación de desactivación)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("Index");

            try
            {
                // Eliminación lógica en MongoDB: Estado = false (simultáneo en Atlas y Local)
                _context.UpdateUsuarioSimultaneo(
                    id,
                    Builders<Usuario>.Update.Set(u => u.Estado, false)
                );

                // Ejecutar Respaldo Integral Completo No Bloqueante
                Services.BackupService.EjecutarRespaldoFullNoBloqueante();

                TempData["MensajeExito"] = "El usuario fue desactivado correctamente (Eliminación Lógica).";
            }
            catch (Exception ex)
            {
                TempData["MensajeError"] = "Error al desactivar usuario: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        #region Helpers
        private IEnumerable<SelectListItem> GetRolesSelectList(string selectedRolId = null)
        {
            var items = new List<SelectListItem>();

            try
            {
                if (_context.IsConnected && _context.Roles != null)
                {
                    var roles = _context.Roles.Find(FilterDefinition<Rol>.Empty).ToList();
                    foreach (var rol in roles)
                    {
                        items.Add(new SelectListItem
                        {
                            Value = rol.Id,
                            Text = $"{rol.NombreRol} ({string.Join(", ", rol.Permisos)})",
                            Selected = (rol.Id == selectedRolId)
                        });
                    }
                }
            }
            catch
            {
                // Si falla la consulta de roles, no romper el formulario
            }

            return items;
        }
        #endregion
    }
}
