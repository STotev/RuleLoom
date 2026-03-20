using BusinessRules.Abstractions;
using BusinessRules.Core.Resolvers;

namespace BusinessRules.Core;

public static class EvaluationContextExtensions
{
    extension(EvaluationContext ctx)
    {
        public object? Resolve(string path, bool useCurrent = true)
        {
            var source = useCurrent ? ctx.Current : ctx.Previous;
        
            if (source == null) return null;

            return new ReflectionValueResolver().Resolve(path, source);
        }
    }
}