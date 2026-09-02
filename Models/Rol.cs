using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Representa un rol y sus permisos de acceso en el sistema.
    /// Colección MongoDB: 'roles'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class Rol
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        /// <summary>
        /// Valores esperados: 'Admin', 'Contador', 'Lectura'
        /// </summary>
        [BsonElement("nombreRol")]
        [Required(ErrorMessage = "El nombre del rol es obligatorio")]
        public string NombreRol { get; set; }

        [BsonElement("permisos")]
        public List<string> Permisos { get; set; } = new List<string>();
    }
}
