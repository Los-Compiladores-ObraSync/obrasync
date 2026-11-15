using System;

namespace ObraSync.Requisiciones.Application.Exceptions
{
    /// <summary>
    /// No se pudo comprobar si la obra está asignada al Capataz porque Usuarios y Obras no respondió
    /// (caído, lento o con error). Es distinto de "la obra no está asignada" (#178).
    /// </summary>
    public class ObraAsignacionNoVerificableException : Exception
    {
        public ObraAsignacionNoVerificableException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
