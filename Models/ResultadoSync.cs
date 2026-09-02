using System;

namespace SistemaContable.Models
{
    /// <summary>
    /// Respuesta detallada del proceso de Auto-Healing Sync.
    /// </summary>
    public class ResultadoSync
    {
        public bool Success { get; set; }
        public string Mensaje { get; set; }
        public string FuenteUtilizada { get; set; }
        public DateTime? FechaDatosRestauradosEC { get; set; }
        public DateTime FechaEjecucionEC { get; set; }

        public string AtlasEstado { get; set; } = "Sin cambios";
        public string LocalEstado { get; set; } = "Sin cambios";
        public string JsonEstado { get; set; } = "Sin cambios";

        public int TotalUsuarios { get; set; }
        public int TotalRoles { get; set; }
        public int TotalCuentas { get; set; }
        public int TotalAsientos { get; set; }
        public int TotalNotificaciones { get; set; }
        public int TotalDocumentos => TotalUsuarios + TotalRoles + TotalCuentas + TotalAsientos + TotalNotificaciones;
    }
}
