using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace SistemaContable.Models
{
    /// <summary>
    /// ViewModel para la creación y edición de usuarios con soporte para hash de contraseñas y listas desplegables.
    /// </summary>
    public class UsuarioFormViewModel
    {
        public string Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
        [Display(Name = "Nombre Completo")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
        [Display(Name = "Correo Electrónico")]
        public string Correo { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un rol.")]
        [Display(Name = "Rol Asignado")]
        public string RolId { get; set; }

        [Display(Name = "Empresa Asignada")]
        public string Empresa { get; set; } = "Empresa Principal S.A.";

        [Display(Name = "Usuario Activo")]
        public bool Estado { get; set; } = true;

        // Propiedad auxiliar para saber si es modo Edición
        public bool EsEdicion => !string.IsNullOrEmpty(Id);

        // Lista de roles para el SelectList de la vista
        public IEnumerable<SelectListItem> RolesDisponibles { get; set; }
    }

    /// <summary>
    /// Modelo DTO para visualización en tabla con datos del Rol combinados.
    /// </summary>
    public class UsuarioItemDto
    {
        public string Id { get; set; }
        public string Nombre { get; set; }
        public string Correo { get; set; }
        public string RolId { get; set; }
        public string NombreRol { get; set; }
        public string Empresa { get; set; }
        public bool Estado { get; set; }
        public System.DateTime FechaCreacion { get; set; }
    }
}
