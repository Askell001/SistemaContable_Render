using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using SistemaContable.Models;

namespace SistemaContable.Data
{
    /// <summary>
    /// Contexto de datos centralizado exclusivo con MongoDB Atlas (Nube).
    /// Optimizado para despliegues en la nube (Render, AWS, Azure) sin dependencias locales.
    /// </summary>
    public sealed class MongoDbContext
    {
        private static readonly Lazy<MongoDbContext> _instance =
            new Lazy<MongoDbContext>(() => new MongoDbContext());

        private readonly IMongoClient _clientAtlas;
        private readonly IMongoDatabase _databaseAtlas;
        private volatile bool _isAtlasConnected;
        private readonly string _errorAtlas;
        private readonly string _databaseName;

        /// <summary>
        /// Instancia Singleton de MongoDbContext.
        /// </summary>
        public static MongoDbContext Instance => _instance.Value;

        /// <summary>
        /// Base de datos principal de lectura y escritura en MongoDB Atlas.
        /// </summary>
        public IMongoDatabase Database => _databaseAtlas;

        /// <summary>
        /// Base de datos Atlas (Nube).
        /// </summary>
        public IMongoDatabase DatabaseAtlas => _databaseAtlas;

        /// <summary>
        /// Compatibilidad: apunta a la base de datos principal de Atlas.
        /// </summary>
        public IMongoDatabase DatabaseLocal => _databaseAtlas;

        /// <summary>
        /// Indica si la conexión con MongoDB Atlas está activa.
        /// </summary>
        public bool IsConnected => _isAtlasConnected;

        public bool IsAtlasConnected => _isAtlasConnected;
        public bool IsLocalConnected => _isAtlasConnected;
        public bool IsSimultaneousSync => _isAtlasConnected;

        public string ActiveConnectionName => _isAtlasConnected ? "MongoDB Atlas (Nube)" : "Desconectado";
        public string DatabaseName => _databaseName;
        public string LastErrorMessage => !_isAtlasConnected ? _errorAtlas : null;

        // ================= Colecciones Tipadas =================
        public IMongoCollection<Usuario> Usuarios => _databaseAtlas?.GetCollection<Usuario>("usuarios");
        public IMongoCollection<Rol> Roles => _databaseAtlas?.GetCollection<Rol>("roles");
        public IMongoCollection<Notificacion> Notificaciones => _databaseAtlas?.GetCollection<Notificacion>("notificaciones");
        public IMongoCollection<CuentaContable> CuentasContables => _databaseAtlas?.GetCollection<CuentaContable>("cuentasContables");
        public IMongoCollection<AsientoContable> AsientosContables => _databaseAtlas?.GetCollection<AsientoContable>("asientosContables");
        public IMongoCollection<ControlSincronizacion> ControlSincronizacion => _databaseAtlas?.GetCollection<ControlSincronizacion>("controlSincronizacion");

        // Alias de compatibilidad
        public IMongoCollection<Usuario> ColUsuariosAtlas => Usuarios;
        public IMongoCollection<Rol> ColRolesAtlas => Roles;
        public IMongoCollection<Notificacion> ColNotificacionesAtlas => Notificaciones;
        public IMongoCollection<CuentaContable> ColCuentasAtlas => CuentasContables;
        public IMongoCollection<AsientoContable> ColAsientosAtlas => AsientosContables;
        public IMongoCollection<ControlSincronizacion> ColControlAtlas => ControlSincronizacion;

        public IMongoCollection<Usuario> ColUsuariosLocal => Usuarios;
        public IMongoCollection<Rol> ColRolesLocal => Roles;
        public IMongoCollection<Notificacion> ColNotificacionesLocal => Notificaciones;
        public IMongoCollection<CuentaContable> ColCuentasLocal => CuentasContables;
        public IMongoCollection<AsientoContable> ColAsientosLocal => AsientosContables;
        public IMongoCollection<ControlSincronizacion> ColControlLocal => ControlSincronizacion;

        private MongoDbContext()
        {
            _databaseName = ConfigurationManager.AppSettings["MongoDatabaseName"] ?? "ContabilidadDB";
            var pingCmd = new BsonDocument("ping", 1);

            try
            {
                var connAtlas = Environment.GetEnvironmentVariable("MongoAtlas") 
                    ?? ConfigurationManager.ConnectionStrings["MongoAtlas"]?.ConnectionString
                    ?? "mongodb+srv://user:12345@registrousuarios.e6jeny6.mongodb.net/";

                if (!string.IsNullOrWhiteSpace(connAtlas))
                {
                    var settingsAtlas = MongoClientSettings.FromUrl(new MongoUrl(connAtlas));
                    settingsAtlas.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                    settingsAtlas.ConnectTimeout = TimeSpan.FromSeconds(5);
                    settingsAtlas.SocketTimeout = TimeSpan.FromSeconds(10);
                    settingsAtlas.ApplicationName = "SistemaContableCloud";

                    _clientAtlas = new MongoClient(settingsAtlas);
                    _databaseAtlas = _clientAtlas.GetDatabase(_databaseName);
                    _databaseAtlas.RunCommand<BsonDocument>(pingCmd);
                    _isAtlasConnected = true;
                    Trace.WriteLine("[MongoDbContext] Conexión establecida con MongoDB Atlas (Nube).");
                }
            }
            catch (Exception ex)
            {
                _isAtlasConnected = false;
                _errorAtlas = ex.Message;
                Trace.TraceWarning($"[MongoDbContext] Aviso al conectar con MongoAtlas: {ex.Message}");
            }
        }

        #region Métodos de Persistencia en Atlas

        // ================= ASIENTOS CONTABLES =================
        public void InsertAsientoSimultaneo(AsientoContable asiento)
        {
            if (string.IsNullOrEmpty(asiento.Id))
            {
                asiento.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () => ColAsientosAtlas?.ReplaceOne(a => a.Id == asiento.Id, asiento, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Asiento {asiento.NumeroAsiento}"
            );
        }

        public void UpdateAsientoSimultaneo(string id, UpdateDefinition<AsientoContable> update)
        {
            EjecutarAccion(
                () => ColAsientosAtlas?.UpdateOne(a => a.Id == id, update),
                $"Update Asiento {id}"
            );
        }

        public void DeleteAsientoSimultaneo(string id)
        {
            EjecutarAccion(
                () => ColAsientosAtlas?.DeleteOne(a => a.Id == id),
                $"Delete Asiento {id}"
            );
        }

        // ================= CUENTAS CONTABLES =================
        public void InsertCuentaSimultanea(CuentaContable cuenta)
        {
            if (string.IsNullOrEmpty(cuenta.Id))
            {
                cuenta.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () => ColCuentasAtlas?.ReplaceOne(c => c.Id == cuenta.Id, cuenta, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Cuenta {cuenta.Codigo}"
            );
        }

        public void InsertManyCuentasSimultaneo(IEnumerable<CuentaContable> cuentas)
        {
            var lista = cuentas.ToList();
            foreach (var c in lista)
            {
                if (string.IsNullOrEmpty(c.Id)) c.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () =>
                {
                    if (ColCuentasAtlas != null)
                    {
                        foreach (var c in lista)
                        {
                            ColCuentasAtlas.ReplaceOne(x => x.Id == c.Id, c, new ReplaceOptions { IsUpsert = true });
                        }
                    }
                },
                $"InsertMany Cuentas ({lista.Count})"
            );
        }

        public void UpdateCuentaSimultanea(string id, UpdateDefinition<CuentaContable> update)
        {
            EjecutarAccion(
                () => ColCuentasAtlas?.UpdateOne(c => c.Id == id, update),
                $"Update Cuenta {id}"
            );
        }

        public void DeleteCuentaSimultanea(string id)
        {
            EjecutarAccion(
                () => ColCuentasAtlas?.DeleteOne(c => c.Id == id),
                $"Delete Cuenta {id}"
            );
        }

        // ================= USUARIOS =================
        public void InsertUsuarioSimultaneo(Usuario usuario)
        {
            if (string.IsNullOrEmpty(usuario.Id))
            {
                usuario.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () => ColUsuariosAtlas?.ReplaceOne(u => u.Id == usuario.Id, usuario, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Usuario {usuario.Correo}"
            );
        }

        public void UpdateUsuarioSimultaneo(string id, UpdateDefinition<Usuario> update)
        {
            EjecutarAccion(
                () => ColUsuariosAtlas?.UpdateOne(u => u.Id == id, update),
                $"Update Usuario {id}"
            );
        }

        // ================= ROLES =================
        public void InsertRolSimultaneo(Rol rol)
        {
            if (string.IsNullOrEmpty(rol.Id))
            {
                rol.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () => ColRolesAtlas?.ReplaceOne(r => r.Id == rol.Id, rol, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Rol {rol.NombreRol}"
            );
        }

        // ================= NOTIFICACIONES =================
        public void InsertNotificacionSimultanea(Notificacion notificacion)
        {
            if (string.IsNullOrEmpty(notificacion.Id))
            {
                notificacion.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarAccion(
                () => ColNotificacionesAtlas?.ReplaceOne(n => n.Id == notificacion.Id, notificacion, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Notificacion {notificacion.Mensaje}"
            );
        }

        public void UpdateNotificacionSimultanea(string id, UpdateDefinition<Notificacion> update)
        {
            EjecutarAccion(
                () => ColNotificacionesAtlas?.UpdateOne(n => n.Id == id, update),
                $"Update Notificacion {id}"
            );
        }

        public void UpdateManyNotificacionesSimultaneo(FilterDefinition<Notificacion> filter, UpdateDefinition<Notificacion> update)
        {
            EjecutarAccion(
                () => ColNotificacionesAtlas?.UpdateMany(filter, update),
                "UpdateMany Notificaciones"
            );
        }

        /// <summary>
        /// Ejecuta una acción de base de datos en MongoDB Atlas y actualiza auditoría.
        /// </summary>
        private void EjecutarAccion(Action accionAtlas, string descripcionOperacion)
        {
            if (_isAtlasConnected && accionAtlas != null)
            {
                try
                {
                    accionAtlas();
                    Trace.WriteLine($"[MongoDbContext] Persistencia ({descripcionOperacion}) -> Atlas: OK");
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[MongoDbContext] Error en Atlas ({descripcionOperacion}): {ex.Message}");
                }
            }

            if (!descripcionOperacion.StartsWith("ControlSync"))
            {
                ActualizarControlSincronizacionSimultaneo(descripcionOperacion);
            }
        }

        /// <summary>
        /// Actualiza la marca de tiempo de sincronización con la hora oficial de Ecuador en MongoDB Atlas.
        /// </summary>
        public void ActualizarControlSincronizacionSimultaneo(string accion, string origen = "Web")
        {
            try
            {
                var tzEcuador = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var fechaEC = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzEcuador);

                var ctrl = new ControlSincronizacion
                {
                    Id = "66ca00000000000000000001",
                    UltimaModificacionEC = fechaEC,
                    UltimaModificacionUtc = DateTime.UtcNow,
                    OrigenUltimoCambio = origen,
                    DetalleAccion = accion
                };

                if (_isAtlasConnected && ColControlAtlas != null)
                {
                    ColControlAtlas.ReplaceOne(x => x.Id == ctrl.Id, ctrl, new ReplaceOptions { IsUpsert = true });
                }
            }
            catch { }
        }

        #endregion

        /// <summary>
        /// Realiza un ping activo a MongoDB Atlas para diagnóstico en tiempo real.
        /// </summary>
        public (bool Success, long ElapsedMs, string Message) TestConnection()
        {
            if (!IsConnected)
            {
                return (false, 0, LastErrorMessage ?? "No se inicializó ninguna conexión con MongoDB Atlas.");
            }

            var sw = Stopwatch.StartNew();
            var pingCmd = new BsonDocument("ping", 1);

            try
            {
                _databaseAtlas.RunCommand<BsonDocument>(pingCmd);
                _isAtlasConnected = true;
                sw.Stop();
                return (true, sw.ElapsedMilliseconds, "MongoDB Atlas (Nube): OK");
            }
            catch (Exception ex)
            {
                _isAtlasConnected = false;
                sw.Stop();
                return (false, sw.ElapsedMilliseconds, $"MongoDB Atlas: Falló ({ex.Message})");
            }
        }
    }
}
