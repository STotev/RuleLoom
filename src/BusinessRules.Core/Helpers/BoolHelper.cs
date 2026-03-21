namespace BusinessRules.Core.Helpers;

public static class BoolHelper
{
    public static bool ToBool(object? value)
    {
        return value switch
        {
            bool b => b,
            null => false,
            _ => throw new InvalidOperationException($"Cannot convert {value} to bool")
        };
    }
}
