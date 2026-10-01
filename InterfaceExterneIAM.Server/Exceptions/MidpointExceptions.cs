namespace InterfaceExterneIAM.Server.Exceptions;

public class MidpointUnavailableException : Exception
{
    public MidpointUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public class MidpointAuthenticationException : Exception
{
    public MidpointAuthenticationException(string message, Exception? inner = null) : base(message, inner) { }
}

public class MidpointUserNotFoundException : Exception
{
    public MidpointUserNotFoundException(string message, Exception? inner = null) : base(message, inner) { }
}
