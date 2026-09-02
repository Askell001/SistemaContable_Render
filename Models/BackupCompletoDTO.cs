using System;
using System.Collections.Generic;

namespace SistemaContable.Models
{
    /// <summary>
    /// Estructura DTO que encapsula la totalidad de las entidades de la base de datos para respaldo integral.
    /// </summary>
    public class BackupCompletoDTO
    {
        public string VersionSistema { get; set; } = "1.0.0";
        public DateTime FechaRespaldoEC { get; set; }
        public string OrigenDatos { get; set; } = "MongoDB Atlas";
        public string BaseDeDatosOrigen { get; set; } = "ContabilidadDB";

        public DateTime UltimaModificacionEC { get; set; }
        public ControlSincronizacion ControlSincronizacion { get; set; }

        public List<Usuario> Usuarios { get; set; } = new List<Usuario>();
        public List<Rol> Roles { get; set; } = new List<Rol>();
        public List<Notificacion> Notificaciones { get; set; } = new List<Notificacion>();
        public List<CuentaContable> PlanCuentas { get; set; } = new List<CuentaContable>();
        public List<AsientoContable> AsientosContables { get; set; } = new List<AsientoContable>();

        public int TotalDocumentos => 
            (Usuarios?.Count ?? 0) + 
            (Roles?.Count ?? 0) + 
            (Notificaciones?.Count ?? 0) + 
            (PlanCuentas?.Count ?? 0) + 
            (AsientosContables?.Count ?? 0);
    }
}
