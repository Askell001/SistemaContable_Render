using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace SistemaContable.Models
{
    /// <summary>
    /// ViewModel para la creación de asientos contables con validación de partida doble y detalles dinámicos.
    /// </summary>
    public class AsientoFormViewModel
    {
        public string Id { get; set; }

        [Required(ErrorMessage = "El número de asiento es obligatorio.")]
        [Display(Name = "N° Asiento")]
        public int NumeroAsiento { get; set; }

        [Required(ErrorMessage = "La fecha es obligatoria.")]
        [DataType(DataType.Date)]
        [Display(Name = "Fecha Contable")]
        public DateTime Fecha { get; set; } = DateTime.Today;

        [Display(Name = "Tipo de Asiento")]
        public string Tipo { get; set; } = "Ingreso"; // 'Ingreso', 'Egreso', 'Diario', 'Ajuste'

        [Required(ErrorMessage = "El concepto o glosa es obligatorio.")]
        [StringLength(300, ErrorMessage = "El concepto no puede superar los 300 caracteres.")]
        [Display(Name = "Concepto / Glosa")]
        public string Concepto { get; set; }

        [Required(ErrorMessage = "El estado es obligatorio.")]
        [Display(Name = "Estado")]
        public string Estado { get; set; } = "Aprobado"; // 'Borrador' o 'Aprobado'

        [Display(Name = "Empresa")]
        public string Empresa { get; set; } = "Empresa Principal S.A.";

        /// <summary>
        /// Líneas de detalle del asiento contable.
        /// </summary>
        public List<DetalleAsientoFormModel> Detalles { get; set; } = new List<DetalleAsientoFormModel>();

        /// <summary>
        /// Catálogo de cuentas contables disponibles para las listas desplegables.
        /// </summary>
        public IEnumerable<SelectListItem> CuentasDisponibles { get; set; }
    }

    /// <summary>
    /// Modelo de fila para cada movimiento contable (Debe / Haber).
    /// </summary>
    public class DetalleAsientoFormModel
    {
        [Required(ErrorMessage = "La cuenta contable es obligatoria.")]
        public string CuentaId { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "El valor del Debe no puede ser negativo.")]
        public decimal Debe { get; set; } = 0m;

        [Range(0, double.MaxValue, ErrorMessage = "El valor del Haber no puede ser negativo.")]
        public decimal Haber { get; set; } = 0m;
    }

    /// <summary>
    /// DTO para la visualización del listado y detalle del comprobante de diario.
    /// </summary>
    public class AsientoComprobanteDto
    {
        public string Id { get; set; }
        public int NumeroAsiento { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public string UsuarioId { get; set; }
        public string NombreUsuario { get; set; }
        public string Empresa { get; set; }
        public string Estado { get; set; }
        public decimal TotalDebe { get; set; }
        public decimal TotalHaber { get; set; }
        public List<DetalleLineaDto> Lineas { get; set; } = new List<DetalleLineaDto>();
    }

    public class DetalleLineaDto
    {
        public string CuentaId { get; set; }
        public string CodigoCuenta { get; set; }
        public string NombreCuenta { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
    }
}
