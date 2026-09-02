using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Representa un usuario dentro del sistema contable.
    /// Colección MongoDB: 'usuarios'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class Usuario
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("nombre")]
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
        public string Nombre { get; set; }

        [BsonElement("correo")]
        [Required(ErrorMessage = "El correo electrónico es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de correo electrónico inválido")]
        public string Correo { get; set; }

        [BsonElement("passwordHash")]
        [Required]
        public string PasswordHash { get; set; }

        [BsonElement("rolId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [Required(ErrorMessage = "El rol es obligatorio")]
        public string RolId { get; set; }

        [BsonElement("empresa")]
        [Display(Name = "Empresa Asignada")]
        public string Empresa { get; set; } = "Empresa Principal S.A.";

        [BsonElement("estado")]
        public bool Estado { get; set; } = true;

        [BsonElement("fechaCreacion")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    }
}
