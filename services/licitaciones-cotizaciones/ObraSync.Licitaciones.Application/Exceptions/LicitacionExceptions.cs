namespace ObraSync.Licitaciones.Application.Exceptions;

public class ValidacionException : Exception { public ValidacionException(string m) : base(m) { } }   // 400
public class NoEncontradoException : Exception { public NoEncontradoException(string m) : base(m) { } } // 404
public class ConflictoException : Exception { public ConflictoException(string m) : base(m) { } }       // 409