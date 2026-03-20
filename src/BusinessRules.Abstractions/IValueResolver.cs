namespace BusinessRules.Abstractions;

public interface IValueResolver
{
    object? Resolve(string path, object obj);
}