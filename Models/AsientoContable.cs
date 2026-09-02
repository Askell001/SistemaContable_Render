using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Representa un asiento contable o póliza de diario con partida doble.
    /// Colección MongoDB: 'asientosContables'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class AsientoContable
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("numeroAsiento")]
        [Required]
        public int NumeroAsiento { get; set; }

        [BsonElement("fecha")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [BsonElement("concepto")]
        [Required(ErrorMessage = "El concepto o glosa es obligatorio")]
        [StringLength(300)]
        public string Concepto { get; set; }

        [BsonElement("usuarioId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [Required]
        public string UsuarioId { get; set; }

        [BsonElement("empresa")]
        public string Empresa { get; set; } = "Empresa Principal S.A.";

        /// <summary>
        /// Valores esperados: 'Borrador', 'Aprobado', 'Anulado'
        /// </summary>
        [BsonElement("estado")]
        [Required]
        public string Estado { get; set; } = "Borrador";

        /// <summary>
        /// Líneas de detalle del asiento (Cuentas, Debe y Haber)
        /// </summary>
        [BsonElement("detalles")]
        public List<DetalleAsientoContable> Detalles { get; set; } = new List<DetalleAsientoContable>();
    }

    /// <summary>
    /// Objeto embebido que representa cada movimiento dentro del asiento contable.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class DetalleAsientoContable
    {
        [BsonElement("cuentaId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [Required(ErrorMessage = "La cuenta contable es obligatoria")]
        public string CuentaId { get; set; }

        [BsonElement("debe")]
        [BsonRepresentation(BsonType.Decimal128)]
        [Range(0, double.MaxValue, ErrorMessage = "El valor del Debe no puede ser negativo")]
        public decimal Debe { get; set; } = 0m;

        [BsonElement("haber")]
        [BsonRepresentation(BsonType.Decimal128)]
        [Range(0, double.MaxValue, ErrorMessage = "El valor del Haber no puede ser negativo")]
        public decimal Haber { get; set; } = 0m;
    }
}
