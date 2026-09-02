using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Representa las notificaciones dirigidas a usuarios en el sistema contable.
    /// Colección MongoDB: 'notificaciones'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class Notificacion
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("usuarioId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [Required]
        public string UsuarioId { get; set; }

        [BsonElement("mensaje")]
        [Required(ErrorMessage = "El mensaje es obligatorio")]
        [StringLength(500)]
        public string Mensaje { get; set; }

        /// <summary>
        /// Valores esperados: 'Info', 'Alerta', 'Exito'
        /// </summary>
        [BsonElement("tipo")]
        [Required]
        public string Tipo { get; set; } = "Info";

        [BsonElement("leida")]
        public bool Leida { get; set; } = false;

        [BsonElement("fecha")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime Fecha { get; set; } = DateTime.UtcNow;
    }
}
