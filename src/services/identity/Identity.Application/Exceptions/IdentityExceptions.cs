namespace Identity.Application.Exceptions;

public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string email) : base($"A user with email '{email}' already exists.") { }
}

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid email or password.") { }
}

public sealed class RoleNotFoundException : Exception
{
    public RoleNotFoundException(string role) : base($"Role '{role}' is not seeded in the database.") { }
}