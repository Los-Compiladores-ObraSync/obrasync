using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Hosting;
using ObraSync.Recepcion.Api.Controllers;

namespace ObraSync.Recepcion.Api.Conventions;

/// <summary>
/// Remueve controladores exclusivos de prueba y desarrollo (como TestController)
/// cuando la aplicación se ejecuta en entornos productivos o fuera de Development/Testing.
/// </summary>
public class SoloEntornoDesarrolloConvention : IApplicationModelConvention
{
    private readonly IHostEnvironment _environment;

    public SoloEntornoDesarrolloConvention(IHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public void Apply(ApplicationModel application)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            var testControllers = application.Controllers
                .Where(c => c.ControllerType == typeof(TestController))
                .ToList();

            foreach (var controller in testControllers)
            {
                application.Controllers.Remove(controller);
            }
        }
    }
}
