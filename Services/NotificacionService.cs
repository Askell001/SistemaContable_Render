using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MongoDB.Driver;
using SistemaContable.Data;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio centralizado para la gestión y despacho de notificaciones a usuarios en MongoDB.
    /// </summary>
    public class NotificacionService
    {
        private readonly MongoDbContext _context;

        public NotificacionService()
        {
            _context = MongoDbContext.Instance;
        }

        /// <summary>
        /// Registra una nueva notificación para un usuario específico en la colección 'notificaciones'.
        /// </summary>
        /// <param name="usuarioId">ID de MongoDB del usuario destinatario.</param>
        /// <param name="mensaje">Contenido o descripción de la notificación.</param>
        /// <param name="tipo">Tipo de alerta: 'Info', 'Alerta', 'Exito'.</param>
        /// <returns>True si se registró correctamente; False en caso contrario.</returns>
        public bool CrearNotificacion(string usuarioId, string mensaje, string tipo = "Info")
        {
            if (string.IsNullOrWhiteSpace(usuarioId) || string.IsNullOrWhiteSpace(mensaje))
            {
                return false;
            }

            try
            {
                if (!_context.IsConnected || _context.Notificaciones == null)
                {
                    Trace.TraceWarning("[NotificacionService] Contexto de MongoDB no disponible.");
                    return false;
                }

                var notificacion = new Notificacion
                {
                    UsuarioId = usuarioId,
                    Mensaje = mensaje.Trim(),
                    Tipo = normalizarTipo(tipo),
                    Leida = false,
                    Fecha = DateTime.UtcNow
                };

                _context.InsertNotificacionSimultanea(notificacion);
                Trace.WriteLine($"[NotificacionService] Notificación enviada a usuario {usuarioId}: {mensaje}");
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al crear notificación: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Inserta una notificación para todos los usuarios activos del sistema.
        /// </summary>
        public int CrearNotificacionATodos(string mensaje, string tipo = "Info")
        {
            int enviadas = 0;
            try
            {
                if (!_context.IsConnected || _context.Usuarios == null) return 0;

                var usuariosActivos = _context.Usuarios.Find(u => u.Estado == true).ToList();
                foreach (var usuario in usuariosActivos)
                {
                    if (CrearNotificacion(usuario.Id, mensaje, tipo))
                    {
                        enviadas++;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al notificar a todos: {ex.Message}");
            }
            return enviadas;
        }

        /// <summary>
        /// Obtiene las notificaciones no leídas de un usuario ordenadas descendentemente por fecha.
        /// </summary>
        public List<Notificacion> ObtenerNoLeidasPorUsuario(string usuarioId, int limite = 10)
        {
            if (string.IsNullOrEmpty(usuarioId)) return new List<Notificacion>();

            try
            {
                if (!_context.IsConnected || _context.Notificaciones == null) return new List<Notificacion>();

                return _context.Notificaciones
                    .Find(n => n.UsuarioId == usuarioId && n.Leida == false)
                    .SortByDescending(n => n.Fecha)
                    .Limit(limite)
                    .ToList();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al obtener no leídas: {ex.Message}");
                return new List<Notificacion>();
            }
        }

        /// <summary>
        /// Cuenta la cantidad de notificaciones no leídas para un usuario específico.
        /// </summary>
        public long ContarNoLeidasPorUsuario(string usuarioId)
        {
            if (string.IsNullOrEmpty(usuarioId)) return 0;

            try
            {
                if (!_context.IsConnected || _context.Notificaciones == null) return 0;

                return _context.Notificaciones.CountDocuments(n => n.UsuarioId == usuarioId && n.Leida == false);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al contar no leídas: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Marca una notificación específica como leída.
        /// </summary>
        public bool MarcarComoLeida(string notificacionId)
        {
            if (string.IsNullOrEmpty(notificacionId)) return false;

            try
            {
                if (!_context.IsConnected || _context.Notificaciones == null) return false;

                _context.UpdateNotificacionSimultanea(
                    notificacionId,
                    Builders<Notificacion>.Update.Set(n => n.Leida, true)
                );
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al marcar leída: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Marca todas las notificaciones pendientes de un usuario como leídas.
        /// </summary>
        public bool MarcarTodasComoLeidas(string usuarioId)
        {
            if (string.IsNullOrEmpty(usuarioId)) return false;

            try
            {
                if (!_context.IsConnected || _context.Notificaciones == null) return false;

                _context.UpdateManyNotificacionesSimultaneo(
                    Builders<Notificacion>.Filter.Where(n => n.UsuarioId == usuarioId && n.Leida == false),
                    Builders<Notificacion>.Update.Set(n => n.Leida, true)
                );
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NotificacionService] Error al marcar todas como leídas: {ex.Message}");
                return false;
            }
        }

        #region Helpers
        private string normalizarTipo(string tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo)) return "Info";
            tipo = tipo.Trim();
            if (tipo.Equals("Alerta", StringComparison.OrdinalIgnoreCase)) return "Alerta";
            if (tipo.Equals("Exito", StringComparison.OrdinalIgnoreCase)) return "Exito";
            return "Info";
        }
        #endregion
    }
}
