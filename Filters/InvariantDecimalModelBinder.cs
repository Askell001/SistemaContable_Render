using System;
using System.Globalization;
using System.Web.Mvc;

namespace SistemaContable.Filters
{
    /// <summary>
    /// Model Binder personalizado para decimales que acepta tanto coma (,) como punto (.) de forma transparente.
    /// Evita errores de conversión por globalización regional (es-ES vs InvariantCulture).
    /// </summary>
    public class InvariantDecimalModelBinder : IModelBinder
    {
        public object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueResult == null || string.IsNullOrWhiteSpace(valueResult.AttemptedValue))
            {
                return 0m;
            }

            var attemptedValue = valueResult.AttemptedValue.Trim();

            // Quitar espacios y símbolos de moneda
            attemptedValue = attemptedValue.Replace(" ", "").Replace("$", "").Replace("€", "").Replace("S/.", "").Replace("S/", "");

            // Si contiene coma y punto (ej 1,000.50 o 1.000,50)
            if (attemptedValue.Contains(",") && attemptedValue.Contains("."))
            {
                if (attemptedValue.IndexOf(",") < attemptedValue.IndexOf("."))
                {
                    // Formato 1,000.50 (coma es separador de miles)
                    attemptedValue = attemptedValue.Replace(",", "");
                }
                else
                {
                    // Formato 1.000,50 (punto es miles, coma es decimal)
                    attemptedValue = attemptedValue.Replace(".", "").Replace(",", ".");
                }
            }
            else if (attemptedValue.Contains(","))
            {
                // Formato 847,46 -> 847.46
                attemptedValue = attemptedValue.Replace(",", ".");
            }

            if (decimal.TryParse(attemptedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            // Fallback con cultura actual
            if (decimal.TryParse(valueResult.AttemptedValue, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal resultCurrent))
            {
                return resultCurrent;
            }

            bindingContext.ModelState.AddModelError(bindingContext.ModelName, $"El valor '{valueResult.AttemptedValue}' no es un número decimal válido.");
            return 0m;
        }
    }
}
