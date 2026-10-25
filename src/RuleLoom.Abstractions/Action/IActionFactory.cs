namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Creates actions. The engine finds an action's registration in its <see cref="Catalog"/> by name and asks the
/// factory for an instance, so the factory never resolves names: implement it to create actions your own way (e.g.
/// from another container).
/// </summary>
public interface IActionFactory
{
    /// <summary>
    /// Creates (or returns) the action for <paramref name="action"/>.
    /// </summary>
    /// <param name="action">The action's registration in the catalog.</param>
    /// <exception cref="InvalidOperationException">The action can't be created.</exception>
    IAction Create(ActionRegistration action);
}
