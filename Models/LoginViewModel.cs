using System.ComponentModel.DataAnnotations;

namespace SistemaContable.Models
{
    /// <summary>
    /// ViewModel para la autenticación de usuarios en el sistema.
    /// </summary>
    public class LoginViewModel
    {
        [Required(ErrorMessage = "El correo electrónico es requerido.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
        [Display(Name = "Correo Electrónico")]
        public string Correo { get; set; }

        [Required(ErrorMessage = "La contraseña es requerida.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [Display(Name = "Recordar sesión")]
        public bool RememberMe { get; set; } = false;
    }
}
