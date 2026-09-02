using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Representa una cuenta dentro del catálogo o plan de cuentas contable.
    /// Colección MongoDB: 'cuentasContables'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class CuentaContable
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        /// <summary>
        /// Código contable jerárquico (ej. '1.1.01', '1.1.01.01')
        /// </summary>
        [BsonElement("codigo")]
        [Required(ErrorMessage = "El código contable es obligatorio")]
        public string Codigo { get; set; }

        [BsonElement("nombre")]
        [Required(ErrorMessage = "El nombre de la cuenta es obligatorio")]
        [StringLength(150)]
        public string Nombre { get; set; }

        /// <summary>
        /// Tipo de cuenta: 'Activo', 'Pasivo', 'Patrimonio', 'Ingreso', 'Gasto'
        /// </summary>
        [BsonElement("tipo")]
        [Required(ErrorMessage = "El tipo de cuenta es obligatorio")]
        public string Tipo { get; set; }

        /// <summary>
        /// Nivel jerárquico en el catálogo (1: Clase, 2: Grupo, 3: Cuenta, 4: Subcuenta, etc.)
        /// </summary>
        [BsonElement("nivel")]
        [Range(1, 10, ErrorMessage = "El nivel debe estar entre 1 y 10")]
        public int Nivel { get; set; } = 1;
    }
}
