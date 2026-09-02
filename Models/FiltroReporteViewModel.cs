using System;
using System.Collections.Generic;
using System.Linq;
using SistemaContable.Models;

namespace SistemaContable.Models
{
    public class FiltroReporteViewModel
    {
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Estado { get; set; } = "Todos";
        public string Formato { get; set; } = "excel"; // "excel", "pdf", "xml"

        // Metadatos de generación (Ecuador)
        public DateTime FechaGeneracionEC { get; set; }
        public string UsuarioNombre { get; set; }
        public string UsuarioCorreo { get; set; }
        public string Empresa { get; set; } = "Empresa Principal S.A.";

        // Resultados
        public List<ReporteAsientoItemDto> Asientos { get; set; } = new List<ReporteAsientoItemDto>();
        public decimal TotalDebeGlobal => Asientos?.Sum(a => a.TotalDebe) ?? 0m;
        public decimal TotalHaberGlobal => Asientos?.Sum(a => a.TotalHaber) ?? 0m;
        public int TotalAsientos => Asientos?.Count ?? 0;
    }

    public class ReporteAsientoItemDto
    {
        public string Id { get; set; }
        public int NumeroAsiento { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public string Estado { get; set; }
        public string Empresa { get; set; }
        public string UsuarioCreadorNombre { get; set; }
        public string UsuarioCreadorCorreo { get; set; }
        public decimal TotalDebe { get; set; }
        public decimal TotalHaber { get; set; }
        public List<ReporteDetalleItemDto> Detalles { get; set; } = new List<ReporteDetalleItemDto>();
    }

    public class ReporteDetalleItemDto
    {
        public string CuentaCodigo { get; set; }
        public string CuentaNombre { get; set; }
        public string CuentaTipo { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
    }
}
