using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SistemaContable.Models
{
    /// <summary>
    /// Documento singleton para auditoría de sincronización y marcas de tiempo en Ecuador.
    /// Colección MongoDB: 'controlSincronizacion'
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ControlSincronizacion
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = "66ca00000000000000000001";

        [BsonElement("ultimaModificacionEC")]
        public DateTime UltimaModificacionEC { get; set; }

        [BsonElement("ultimaModificacionUtc")]
        public DateTime UltimaModificacionUtc { get; set; } = DateTime.UtcNow;

        [BsonElement("origenUltimoCambio")]
        public string OrigenUltimoCambio { get; set; } = "Web";

        [BsonElement("detalleAccion")]
        public string DetalleAccion { get; set; } = "Inicialización";

        [BsonElement("totalDocumentos")]
        public int TotalDocumentos { get; set; }
    }
}
