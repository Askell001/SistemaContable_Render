using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SistemaContable.Models;

namespace SistemaContable.Data
{
    /// <summary>
    /// Contexto de datos MongoDB con persistencia y sincronización simultánea en tiempo real
    /// tanto en MongoDB Atlas (Nube) como en MongoDB Local (localhost:27017).
    /// </summary>
    public sealed class MongoDbContext
    {
        private static readonly Lazy<MongoDbContext> _instance =
            new Lazy<MongoDbContext>(() => new MongoDbContext());

        private readonly IMongoClient _clientAtlas;
        private readonly IMongoDatabase _databaseAtlas;
        private volatile bool _isAtlasConnected;
        private readonly string _errorAtlas;

        private readonly IMongoClient _clientLocal;
        private readonly IMongoDatabase _databaseLocal;
        private volatile bool _isLocalConnected;
        private readonly string _errorLocal;

        private readonly string _activeConfigName;
        private readonly string _databaseName;

        /// <summary>
        /// Instancia Singleton de MongoDbContext.
        /// </summary>
        public static MongoDbContext Instance => _instance.Value;

        /// <summary>
        /// Base de datos principal de lectura/consulta.
        /// Prioriza Localhost cuando está disponible para garantizar soporte 100% offline y latencia 0ms,
        /// con failover automático a Atlas cuando sea necesario.
        /// </summary>
        public IMongoDatabase Database
        {
            get
            {
                if (_isLocalConnected && _databaseLocal != null)
                {
                    return _databaseLocal;
                }
                return _databaseAtlas ?? _databaseLocal;
            }
        }

        /// <summary>
        /// Base de datos de Atlas (Nube).
        /// </summary>
        public IMongoDatabase DatabaseAtlas => _databaseAtlas;

        /// <summary>
        /// Base de datos de Localhost (Local).
        /// </summary>
        public IMongoDatabase DatabaseLocal => _databaseLocal;

        /// <summary>
        /// Indica si al menos una base de datos está conectada.
        /// </summary>
        public bool IsConnected => _isAtlasConnected || _isLocalConnected;

        /// <summary>
        /// Indica si Atlas está conectado y operativo.
        /// </summary>
        public bool IsAtlasConnected => _isAtlasConnected;

        /// <summary>
        /// Indica si Localhost (27017) está conectado y operativo.
        /// </summary>
        public bool IsLocalConnected => _isLocalConnected;

        /// <summary>
        /// Indica si ambas bases de datos están sincronizándose en tiempo real simultáneamente.
        /// </summary>
        public bool IsSimultaneousSync => _isAtlasConnected && _isLocalConnected;

        public string ActiveConnectionName => IsSimultaneousSync 
            ? "Simultáneo (Atlas + Localhost)" 
            : (_isAtlasConnected ? "MongoAtlas (Nube)" : (_isLocalConnected ? "MongoLocal (Localhost)" : "Desconectado"));

        public string DatabaseName => _databaseName;

        public string LastErrorMessage => !_isAtlasConnected && !_isLocalConnected 
            ? $"Atlas: {_errorAtlas} | Local: {_errorLocal}" 
            : null;

        // ================= Colecciones Tipadas de Lectura =================
        public IMongoCollection<Usuario> Usuarios => Database?.GetCollection<Usuario>("usuarios");
        public IMongoCollection<Rol> Roles => Database?.GetCollection<Rol>("roles");
        public IMongoCollection<Notificacion> Notificaciones => Database?.GetCollection<Notificacion>("notificaciones");
        public IMongoCollection<CuentaContable> CuentasContables => Database?.GetCollection<CuentaContable>("cuentasContables");
        public IMongoCollection<AsientoContable> AsientosContables => Database?.GetCollection<AsientoContable>("asientosContables");
        public IMongoCollection<ControlSincronizacion> ControlSincronizacion => Database?.GetCollection<ControlSincronizacion>("controlSincronizacion");

        // Colecciones Atlas
        public IMongoCollection<Usuario> ColUsuariosAtlas => _databaseAtlas?.GetCollection<Usuario>("usuarios");
        public IMongoCollection<Rol> ColRolesAtlas => _databaseAtlas?.GetCollection<Rol>("roles");
        public IMongoCollection<Notificacion> ColNotificacionesAtlas => _databaseAtlas?.GetCollection<Notificacion>("notificaciones");
        public IMongoCollection<CuentaContable> ColCuentasAtlas => _databaseAtlas?.GetCollection<CuentaContable>("cuentasContables");
        public IMongoCollection<AsientoContable> ColAsientosAtlas => _databaseAtlas?.GetCollection<AsientoContable>("asientosContables");
        public IMongoCollection<ControlSincronizacion> ColControlAtlas => _databaseAtlas?.GetCollection<ControlSincronizacion>("controlSincronizacion");

        // Colecciones Local
        public IMongoCollection<Usuario> ColUsuariosLocal => _databaseLocal?.GetCollection<Usuario>("usuarios");
        public IMongoCollection<Rol> ColRolesLocal => _databaseLocal?.GetCollection<Rol>("roles");
        public IMongoCollection<Notificacion> ColNotificacionesLocal => _databaseLocal?.GetCollection<Notificacion>("notificaciones");
        public IMongoCollection<CuentaContable> ColCuentasLocal => _databaseLocal?.GetCollection<CuentaContable>("cuentasContables");
        public IMongoCollection<AsientoContable> ColAsientosLocal => _databaseLocal?.GetCollection<AsientoContable>("asientosContables");
        public IMongoCollection<ControlSincronizacion> ColControlLocal => _databaseLocal?.GetCollection<ControlSincronizacion>("controlSincronizacion");

        private MongoDbContext()
        {
            _activeConfigName = ConfigurationManager.AppSettings["ActiveMongoConnection"] ?? "MongoAtlas";
            _databaseName = ConfigurationManager.AppSettings["MongoDatabaseName"] ?? "ContabilidadDB";
            var pingCmd = new BsonDocument("ping", 1);

            // 1. Inicializar Conexión MongoAtlas con timeouts cortos para fail-fast
            try
            {
                var connAtlas = ConfigurationManager.ConnectionStrings["MongoAtlas"]?.ConnectionString;
                if (!string.IsNullOrWhiteSpace(connAtlas))
                {
                    var settingsAtlas = MongoClientSettings.FromUrl(new MongoUrl(connAtlas));
                    settingsAtlas.ServerSelectionTimeout = TimeSpan.FromMilliseconds(1000);
                    settingsAtlas.ConnectTimeout = TimeSpan.FromMilliseconds(1000);
                    settingsAtlas.SocketTimeout = TimeSpan.FromMilliseconds(2000);
                    settingsAtlas.ApplicationName = "SistemaContable";

                    _clientAtlas = new MongoClient(settingsAtlas);
                    _databaseAtlas = _clientAtlas.GetDatabase(_databaseName);
                    _databaseAtlas.RunCommand<BsonDocument>(pingCmd);
                    _isAtlasConnected = true;
                    Trace.WriteLine("[MongoDbContext] Conexión establecida con MongoAtlas.");
                }
            }
            catch (Exception ex)
            {
                _isAtlasConnected = false;
                _errorAtlas = ex.Message;
                Trace.TraceWarning($"[MongoDbContext] MongoAtlas no disponible al iniciar (Operando en Modo Offline/Local): {ex.Message}");
            }

            // 2. Inicializar Conexión MongoLocal
            try
            {
                var connLocal = ConfigurationManager.ConnectionStrings["MongoLocal"]?.ConnectionString 
                    ?? "mongodb://localhost:27017";

                var settingsLocal = MongoClientSettings.FromUrl(new MongoUrl(connLocal));
                settingsLocal.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
                settingsLocal.ConnectTimeout = TimeSpan.FromSeconds(2);
                settingsLocal.SocketTimeout = TimeSpan.FromSeconds(5);
                settingsLocal.ApplicationName = "SistemaContableLocal";

                _clientLocal = new MongoClient(settingsLocal);
                _databaseLocal = _clientLocal.GetDatabase(_databaseName);
                _databaseLocal.RunCommand<BsonDocument>(pingCmd);
                _isLocalConnected = true;
                Trace.WriteLine("[MongoDbContext] Conexión establecida con MongoLocal (Servicio Local Activo).");
            }
            catch (Exception ex)
            {
                _isLocalConnected = false;
                _errorLocal = ex.Message;
                Trace.TraceWarning($"[MongoDbContext] MongoLocal no disponible: {ex.Message}");
            }

            Trace.WriteLine($"[MongoDbContext] Estado de persistencia: {ActiveConnectionName}");
        }

        #region Métodos de Escritura Simultánea (Atlas + Local)

        // ================= ASIENTOS CONTABLES =================
        public void InsertAsientoSimultaneo(AsientoContable asiento)
        {
            if (string.IsNullOrEmpty(asiento.Id))
            {
                asiento.Id = ObjectId.GenerateNewId().ToString();
            }

            EjecutarDobleAccion(
                () => ColAsientosAtlas?.ReplaceOne(a => a.Id == asiento.Id, asiento, new ReplaceOptions { IsUpsert = true }),
                () => ColAsientosLocal?.ReplaceOne(a => a.Id == asiento.Id, asiento, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Asiento {asiento.NumeroAsiento}"
            );
        }

        public void UpdateAsientoSimultaneo(string id, UpdateDefinition<AsientoContable> update)
        {
            EjecutarDobleAccion(
                () => ColAsientosAtlas?.UpdateOne(a => a.Id == id, update),
                () => ColAsientosLocal?.UpdateOne(a => a.Id == id, update),
                $"Update Asiento {id}"
            );
        }

        public void DeleteAsientoSimultaneo(string id)
        {
            EjecutarDobleAccion(
                () => ColAsientosAtlas?.DeleteOne(a => a.Id == id),
                () => ColAsientosLocal?.DeleteOne(a => a.Id == id),
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

            EjecutarDobleAccion(
                () => ColCuentasAtlas?.ReplaceOne(c => c.Id == cuenta.Id, cuenta, new ReplaceOptions { IsUpsert = true }),
                () => ColCuentasLocal?.ReplaceOne(c => c.Id == cuenta.Id, cuenta, new ReplaceOptions { IsUpsert = true }),
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

            EjecutarDobleAccion(
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
                () =>
                {
                    if (ColCuentasLocal != null)
                    {
                        foreach (var c in lista)
                        {
                            ColCuentasLocal.ReplaceOne(x => x.Id == c.Id, c, new ReplaceOptions { IsUpsert = true });
                        }
                    }
                },
                $"InsertMany Cuentas ({lista.Count})"
            );
        }

        public void UpdateCuentaSimultanea(string id, UpdateDefinition<CuentaContable> update)
        {
            EjecutarDobleAccion(
                () => ColCuentasAtlas?.UpdateOne(c => c.Id == id, update),
                () => ColCuentasLocal?.UpdateOne(c => c.Id == id, update),
                $"Update Cuenta {id}"
            );
        }

        public void DeleteCuentaSimultanea(string id)
        {
            EjecutarDobleAccion(
                () => ColCuentasAtlas?.DeleteOne(c => c.Id == id),
                () => ColCuentasLocal?.DeleteOne(c => c.Id == id),
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

            EjecutarDobleAccion(
                () => ColUsuariosAtlas?.ReplaceOne(u => u.Id == usuario.Id, usuario, new ReplaceOptions { IsUpsert = true }),
                () => ColUsuariosLocal?.ReplaceOne(u => u.Id == usuario.Id, usuario, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Usuario {usuario.Correo}"
            );
        }

        public void UpdateUsuarioSimultaneo(string id, UpdateDefinition<Usuario> update)
        {
            EjecutarDobleAccion(
                () => ColUsuariosAtlas?.UpdateOne(u => u.Id == id, update),
                () => ColUsuariosLocal?.UpdateOne(u => u.Id == id, update),
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

            EjecutarDobleAccion(
                () => ColRolesAtlas?.ReplaceOne(r => r.Id == rol.Id, rol, new ReplaceOptions { IsUpsert = true }),
                () => ColRolesLocal?.ReplaceOne(r => r.Id == rol.Id, rol, new ReplaceOptions { IsUpsert = true }),
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

            EjecutarDobleAccion(
                () => ColNotificacionesAtlas?.ReplaceOne(n => n.Id == notificacion.Id, notificacion, new ReplaceOptions { IsUpsert = true }),
                () => ColNotificacionesLocal?.ReplaceOne(n => n.Id == notificacion.Id, notificacion, new ReplaceOptions { IsUpsert = true }),
                $"Insert/Replace Notificacion {notificacion.Mensaje}"
            );
        }

        public void UpdateNotificacionSimultanea(string id, UpdateDefinition<Notificacion> update)
        {
            EjecutarDobleAccion(
                () => ColNotificacionesAtlas?.UpdateOne(n => n.Id == id, update),
                () => ColNotificacionesLocal?.UpdateOne(n => n.Id == id, update),
                $"Update Notificacion {id}"
            );
        }

        public void UpdateManyNotificacionesSimultaneo(FilterDefinition<Notificacion> filter, UpdateDefinition<Notificacion> update)
        {
            EjecutarDobleAccion(
                () => ColNotificacionesAtlas?.UpdateMany(filter, update),
                () => ColNotificacionesLocal?.UpdateMany(filter, update),
                "UpdateMany Notificaciones"
            );
        }

        /// <summary>
        /// Ejecuta una acción de base de datos en Atlas y en Localhost en paralelo de forma resiliente.
        /// </summary>
        private void EjecutarDobleAccion(Action accionAtlas, Action accionLocal, string descripcionOperacion)
        {
            bool atlasOk = false;
            bool localOk = false;

            if (_isAtlasConnected && accionAtlas != null)
            {
                try
                {
                    accionAtlas();
                    atlasOk = true;
                }
                catch (Exception ex)
                {
                    _isAtlasConnected = false;
                    Trace.TraceWarning($"[MongoDbContext] Atlas no respondió (Modo offline activo): {ex.Message}");
                }
            }

            if (_isLocalConnected && accionLocal != null)
            {
                try
                {
                    accionLocal();
                    localOk = true;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[MongoDbContext] Error en Localhost ({descripcionOperacion}): {ex.Message}");
                }
            }

            Trace.WriteLine($"[MongoDbContext] Persistencia ({descripcionOperacion}) -> Atlas: {(atlasOk ? "OK" : "Fuera de línea")}, Local: {(localOk ? "OK" : "Fallo")}");

            // Actualizar timestamp de auditoría en la colección ControlSincronizacion
            if (!descripcionOperacion.StartsWith("ControlSync"))
            {
                ActualizarControlSincronizacionSimultaneo(descripcionOperacion);
            }
        }

        /// <summary>
        /// Actualiza la marca de tiempo de sincronización con la hora oficial de Ecuador en ambas bases de datos.
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
                    try
                    {
                        ColControlAtlas.ReplaceOne(x => x.Id == ctrl.Id, ctrl, new ReplaceOptions { IsUpsert = true });
                    }
                    catch
                    {
                        _isAtlasConnected = false;
                    }
                }

                if (_isLocalConnected && ColControlLocal != null)
                {
                    try
                    {
                        ColControlLocal.ReplaceOne(x => x.Id == ctrl.Id, ctrl, new ReplaceOptions { IsUpsert = true });
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        /// <summary>
        /// Realiza un ping activo a ambas bases de datos para diagnóstico en tiempo real.
        /// </summary>
        public (bool Success, long ElapsedMs, string Message) TestConnection()
        {
            if (!IsConnected)
            {
                return (false, 0, LastErrorMessage ?? "No se inicializó ninguna conexión con MongoDB.");
            }

            var sw = Stopwatch.StartNew();
            var pingCmd = new BsonDocument("ping", 1);
            string estadoMsg = "";

            if (_databaseAtlas != null)
            {
                try
                {
                    _databaseAtlas.RunCommand<BsonDocument>(pingCmd);
                    _isAtlasConnected = true;
                    estadoMsg += "Atlas: OK";
                }
                catch (Exception)
                {
                    _isAtlasConnected = false;
                    estadoMsg += "Atlas: Fuera de Línea";
                }
            }

            if (_databaseLocal != null)
            {
                try
                {
                    _databaseLocal.RunCommand<BsonDocument>(pingCmd);
                    _isLocalConnected = true;
                    estadoMsg += (estadoMsg.Length > 0 ? " | " : "") + "Localhost: OK";
                }
                catch (Exception ex)
                {
                    _isLocalConnected = false;
                    estadoMsg += (estadoMsg.Length > 0 ? " | " : "") + "Localhost: Falló (" + ex.Message + ")";
                }
            }

            sw.Stop();
            return (IsConnected, sw.ElapsedMilliseconds, $"Sincronización: {estadoMsg}");
        }
    }
}
